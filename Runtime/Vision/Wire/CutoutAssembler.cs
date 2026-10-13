using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Datagrams in, whole <see cref="CutoutFrame"/>s out: the receiving half of the cutout wire (python-sidecar~/cutout_wire.py, class Assembler,
    /// is the other implementation and the two are pinned to the same fixtures). No socket and no thread, so every rule here is testable by feeding
    /// it byte arrays.
    ///
    /// THE RULES. Chunks of one frame share (session, sequence) and each is written at its own offset in the payload; the frame is complete when
    /// every chunk index has arrived once. A chunk of a NEWER sequence abandons an unfinished older frame (a live picture that is late is worse than
    /// one that is missing); a chunk of an OLDER sequence is ignored; a repeated chunk is ignored; a new session id is a new producer and drops the
    /// partial frame. A frame whose chunks do not add up to its payload is refused rather than shown with holes. Everything is bounded by
    /// <see cref="CutoutWire.MaxPayloadBytes"/> and <see cref="CutoutWire.MaxDimension"/>, so a sender that lies cannot make this allocate without
    /// limit, and nothing here throws on bad input.
    ///
    /// ONE FRAME IS ASSEMBLED AT A TIME, in a frame this object owns (<see cref="Frame"/>). When <see cref="Add"/> returns true that frame is
    /// complete: take it with <see cref="SwapFrameWith"/> before the next datagram, which may start overwriting it. A deflated mask is inflated
    /// here, on the receive thread, so the main thread never decompresses; a stream that inflates to anything but the declared size is refused.
    ///
    /// Counters are written by the receive thread and read by any thread (Interlocked), so a panel can show them live.
    /// </summary>
    public sealed class CutoutAssembler {
        private readonly CutoutFrame frame = new CutoutFrame();
        private readonly long[] rejected = new long[CutoutWire.RejectKindCount];

        private bool[] seen = new bool[0];
        private bool hasSession;
        private bool hasSequence;
        private bool assembling;
        private uint session;
        private uint sequence;
        private CutoutHeader first;
        private int remaining;
        private long bytesIn;

        private long completed;
        private long abandoned;
        private long stale;
        private long duplicate;
        private long sessionChanges;
        private CutoutReject lastReject;

        /// <summary>Why the most recent <see cref="Add"/> refused its datagram or frame; None when it did not. Written and read by the receive thread.</summary>
        public CutoutReject LastReject {
            get {
                return lastReject;
            }
        }

        /// <summary>The frame being assembled, or the one just completed when <see cref="Add"/> last returned true.</summary>
        public CutoutFrame Frame {
            get {
                return frame;
            }
        }

        /// <summary>Frames that arrived whole and were valid.</summary>
        public long Completed {
            get {
                return Interlocked.Read(ref completed);
            }
        }

        /// <summary>Unfinished frames dropped because a newer one (or a new session) started.</summary>
        public long Abandoned {
            get {
                return Interlocked.Read(ref abandoned);
            }
        }

        /// <summary>Chunks of a frame older than the one being assembled.</summary>
        public long Stale {
            get {
                return Interlocked.Read(ref stale);
            }
        }

        /// <summary>Chunks that arrived twice.</summary>
        public long Duplicate {
            get {
                return Interlocked.Read(ref duplicate);
            }
        }

        /// <summary>Times the producer session id changed after the first chunk (the sender restarted).</summary>
        public long SessionChanges {
            get {
                return Interlocked.Read(ref sessionChanges);
            }
        }

        /// <summary>Datagrams or frames refused, all reasons together.</summary>
        public long Rejected {
            get {
                long total = 0L;
                int i = 1;
                while (i < rejected.Length) {
                    total = total + Interlocked.Read(ref rejected[i]);
                    i = i + 1;
                }
                return total;
            }
        }

        public long RejectedOf(CutoutReject reason) {
            return Interlocked.Read(ref rejected[(int)reason]);
        }

        /// <summary>Forgets the partial frame and the session, as when the receiver restarts. Counters are kept.</summary>
        public void Reset() {
            hasSession = false;
            hasSequence = false;
            assembling = false;
            remaining = 0;
            bytesIn = 0L;
        }

        /// <summary>Hands the completed frame to <paramref name="other"/> and takes its buffers in exchange. Call right after <see cref="Add"/> returned true.</summary>
        public void SwapFrameWith(CutoutFrame other) {
            frame.SwapWith(other);
        }

        /// <summary>
        /// Feeds one datagram. True when this chunk completed a valid frame, which is then in <see cref="Frame"/>. Never throws on bad input: a
        /// datagram or frame that is refused is counted under its reason (<see cref="RejectedOf"/>) and false is returned.
        /// </summary>
        public bool Add(byte[] data, int length) {
            lastReject = CutoutReject.None;
            CutoutHeader header;
            CutoutReject reason = CutoutWire.TryReadHeader(data, length, out header);
            if (reason != CutoutReject.None) {
                Reject(reason);
                return false;
            }
            if (!hasSession || header.Session != session) {
                if (hasSession) {
                    Interlocked.Increment(ref sessionChanges);
                    if (assembling) {
                        Interlocked.Increment(ref abandoned);
                    }
                }
                session = header.Session;
                hasSession = true;
                hasSequence = false;
                assembling = false;
            }
            if (hasSequence && header.Sequence != sequence) {
                if (CutoutWire.IsNewer(header.Sequence, sequence)) {
                    if (assembling) {
                        Interlocked.Increment(ref abandoned);
                    }
                    assembling = false;
                } else {
                    Interlocked.Increment(ref stale);
                    return false;
                }
            } else if (hasSequence && !assembling) {
                // The frame with this number is already complete (or was refused): a chunk of it arriving again is a duplicate, and must not
                // start a second assembly that can never finish.
                Interlocked.Increment(ref duplicate);
                return false;
            }
            if (!assembling) {
                Begin(header);
            } else if (!SameFrame(first, header)) {
                Reject(CutoutReject.Header);
                return false;
            }
            if (seen[header.ChunkIndex]) {
                Interlocked.Increment(ref duplicate);
                return false;
            }
            Buffer.BlockCopy(data, header.HeaderLength, frame.Payload, header.ChunkOffset, header.ChunkLength);
            seen[header.ChunkIndex] = true;
            remaining = remaining - 1;
            bytesIn = bytesIn + header.ChunkLength;
            if (remaining > 0) {
                return false;
            }
            assembling = false;
            if (bytesIn != first.PayloadLength) {
                // Every chunk index arrived but they overlap or leave a gap. An honest sender cannot produce that; do not show it.
                Reject(CutoutReject.Size);
                return false;
            }
            if (first.MaskEncoding == CutoutMaskEncoding.Deflate && !Inflate()) {
                Reject(CutoutReject.Size);
                return false;
            }
            Interlocked.Increment(ref completed);
            return true;
        }

        private void Begin(CutoutHeader header) {
            sequence = header.Sequence;
            hasSequence = true;
            first = header;
            frame.SetHeader(header);
            frame.EnsurePayloadCapacity(header.PayloadLength);
            if (seen.Length < header.ChunkCount) {
                seen = new bool[header.ChunkCount];
            } else {
                Array.Clear(seen, 0, header.ChunkCount);
            }
            remaining = header.ChunkCount;
            bytesIn = 0L;
            assembling = true;
        }

        /// <summary>The fields every chunk of one frame must agree on (the same list the Python assembler compares).</summary>
        private static bool SameFrame(CutoutHeader a, CutoutHeader b) {
            return a.PayloadLength == b.PayloadLength
                && a.ChunkCount == b.ChunkCount
                && a.ColorEncoding == b.ColorEncoding
                && a.MaskEncoding == b.MaskEncoding
                && a.ColorWidth == b.ColorWidth
                && a.ColorHeight == b.ColorHeight
                && a.MaskWidth == b.MaskWidth
                && a.MaskHeight == b.MaskHeight
                && a.ColorLength == b.ColorLength
                && a.MaskLength == b.MaskLength;
        }

        /// <summary>Inflates the mask section into the frame's own buffer. False for a stream that is broken or inflates to more or less than the header says.</summary>
        private bool Inflate() {
            int expected = first.MaskWidth * first.MaskHeight;
            byte[] target = frame.EnsureInflatedCapacity(expected);
            try {
                using (MemoryStream compressed = new MemoryStream(frame.Payload, first.ColorLength, first.MaskLength, false)) {
                    using (DeflateStream inflater = new DeflateStream(compressed, CompressionMode.Decompress)) {
                        int total = 0;
                        while (total < expected) {
                            int read = inflater.Read(target, total, expected - total);
                            if (read <= 0) {
                                break;
                            }
                            total = total + read;
                        }
                        if (total != expected) {
                            return false;
                        }
                        // One more byte is enough to tell a stream that carries more than the declared size; the rest is never inflated.
                        return inflater.ReadByte() < 0;
                    }
                }
            } catch (InvalidDataException) {
                return false;
            } catch (IOException) {
                return false;
            }
        }

        private void Reject(CutoutReject reason) {
            lastReject = reason;
            Interlocked.Increment(ref rejected[(int)reason]);
        }
    }
}
