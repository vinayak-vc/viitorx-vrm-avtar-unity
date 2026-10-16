using NUnit.Framework;

using VirtualMirror.Tracking.OakD;

namespace VirtualMirror.Tests {
    /// <summary>
    /// F-51 tests: the five status lights. The sidecar has no window, so these lights are the only way a person
    /// can tell a dead sidecar from a loading model from an empty room - which makes every rule here a claim
    /// to the user, and each one is pinned against the situation it describes.
    ///
    /// The clock is passed in, so "moving within 6 s" is tested with numbers, not sleeps.
    /// </summary>
    public sealed class SidecarStatusModelF51Tests {
        private const int Sidecar = 0;
        private const int Watchdog = 1;
        private const int Source = 2;
        private const int Model = 3;
        private const int Tracking = 4;

        private static SupervisorSnapshot Snapshot(string state, long frames, long sent, string live, string note,
                                                   double lineAge, int restarts) {
            SupervisorSnapshot snapshot = new SupervisorSnapshot();
            snapshot.State = state;
            snapshot.Ready = state == "RUNNING";
            snapshot.RestartCount = restarts;
            ChildHeartbeat child = new ChildHeartbeat();
            child.Frames = frames;
            child.Sent = sent;
            child.Live = live;
            child.Note = note;
            child.LineAgeSeconds = lineAge;
            snapshot.Child = child;
            return snapshot;
        }

        private static SupervisorSnapshot Running(long frames, long sent, string live) {
            return Snapshot("RUNNING", frames, sent, live, string.Empty, 1.0, 0);
        }

        private static StatusLight[] Eval(SidecarStatusModel model, double now, SidecarLaunchState launcher,
                                          SupervisorSnapshot snapshot) {
            return model.Evaluate(now, true, launcher, snapshot, 1.0);
        }

        private static void Expect(StatusLight light, StatusLevel level, string textPart) {
            Assert.AreEqual(level, light.Level, light.Label + ": " + light.Text);
            StringAssert.Contains(textPart, light.Text, light.Label);
        }

        // ---------------------------------------------------------------- the state file

        [Test]
        public void ASupervisorStateWithAHeartbeatIsParsed() {
            string json = "{\"SupervisorState\":\"RUNNING\",\"SidecarPid\":5404,\"RestartCount\":2,\"LastExitCode\":2,"
                + "\"BackoffSeconds\":0.0,\"SidecarReady\":true,\"CurrentSessionId\":\"vd1\","
                + "\"Child\":{\"Frames\":2031,\"Sent\":4,\"Skipped\":2026,\"Live\":\"Y\",\"Note\":\"ready\",\"LineAgeSeconds\":1.2}}";
            SupervisorSnapshot snapshot;
            Assert.IsTrue(SupervisorSnapshot.TryParse(json, out snapshot));
            Assert.AreEqual("RUNNING", snapshot.State);
            Assert.IsTrue(snapshot.Ready);
            Assert.AreEqual(2, snapshot.RestartCount);
            Assert.IsTrue(snapshot.HasLastExitCode);
            Assert.AreEqual(2, snapshot.LastExitCode);
            Assert.AreEqual(2031L, snapshot.Child.Frames);
            Assert.AreEqual(4L, snapshot.Child.Sent);
            Assert.AreEqual("Y", snapshot.Child.Live);
            Assert.AreEqual("ready", snapshot.Child.Note);
            Assert.AreEqual(1.2, snapshot.Child.LineAgeSeconds, 1e-9);
        }

        [Test]
        public void AStateFileFromBeforeTheHeartbeatExistedStillParses() {
            // A supervisor that predates F-51 has no Child key: still a usable snapshot, with a null Child.
            SupervisorSnapshot snapshot;
            Assert.IsTrue(SupervisorSnapshot.TryParse(
                "{\"SupervisorState\":\"STARTING\",\"RestartCount\":0,\"LastExitCode\":null,\"SidecarReady\":false}",
                out snapshot));
            Assert.AreEqual("STARTING", snapshot.State);
            Assert.IsNull(snapshot.Child);
            Assert.IsFalse(snapshot.HasLastExitCode, "a null exit code is not 0");
        }

        [Test]
        public void ANullChildAndANullLiveAreNotAnError() {
            SupervisorSnapshot snapshot;
            Assert.IsTrue(SupervisorSnapshot.TryParse(
                "{\"SupervisorState\":\"RUNNING\",\"Child\":null}", out snapshot));
            Assert.IsNull(snapshot.Child);
            Assert.IsTrue(SupervisorSnapshot.TryParse(
                "{\"SupervisorState\":\"RUNNING\",\"Child\":{\"Frames\":5,\"Sent\":5,\"Live\":null,\"Note\":\"\"}}",
                out snapshot));
            Assert.IsNull(snapshot.Child.Live);
        }

        [Test]
        public void GarbageAHalfWrittenFileAndTheWrongFileAreRefusedNotThrown() {
            SupervisorSnapshot snapshot;
            Assert.IsFalse(SupervisorSnapshot.TryParse(null, out snapshot));
            Assert.IsNull(snapshot);
            Assert.IsFalse(SupervisorSnapshot.TryParse(string.Empty, out snapshot));
            Assert.IsFalse(SupervisorSnapshot.TryParse("   ", out snapshot));
            Assert.IsFalse(SupervisorSnapshot.TryParse("{\"SupervisorState\":\"RUN", out snapshot), "half written");
            Assert.IsFalse(SupervisorSnapshot.TryParse("not json at all", out snapshot));
            Assert.IsFalse(SupervisorSnapshot.TryParse("{\"something\":\"else\"}", out snapshot), "no SupervisorState");
            Assert.IsFalse(SupervisorSnapshot.TryParse("[1,2,3]", out snapshot), "not an object");
        }

        [Test]
        public void TheStateFileIsWhereTheSupervisorWritesIt() {
            string path = SidecarStatusModel.StatePathFor("D:/sidecar", string.Empty).Replace('\\', '/');
            Assert.AreEqual("D:/sidecar/evidence/oak_v4/f20b/supervisor_state.json", path);
            string overridden = SidecarStatusModel.StatePathFor("D:/sidecar", "E:/ev").Replace('\\', '/');
            Assert.AreEqual("E:/ev/oak_v4/f20b/supervisor_state.json", overridden,
                            "VIRTUAL_MIRROR_EVIDENCE_DIR moves the supervisor's folder, so it must move this too");
            Assert.AreEqual(string.Empty, SidecarStatusModel.StatePathFor(string.Empty, string.Empty));
            Assert.AreEqual("VIRTUAL_MIRROR_EVIDENCE_DIR", SidecarStatusModel.EvidenceDirEnvironmentVariable,
                            "must match evidence_paths.py");
        }

        // ---------------------------------------------------------------- the lights

        [Test]
        public void NothingStartedIsAllOff() {
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] lights = model.Evaluate(10.0, false, SidecarLaunchState.Disabled, null, 0.0);
            Assert.AreEqual(5, lights.Length);
            Expect(lights[Sidecar], StatusLevel.Off, "not started by Unity");
            Expect(lights[Watchdog], StatusLevel.Off, "no supervisor");
            foreach (StatusLight light in lights) {
                Assert.AreEqual(StatusLevel.Off, light.Level, light.Label);
            }
        }

        [Test]
        public void TheOrderAndNamesOfTheLightsAreFixed() {
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] lights = model.Evaluate(0.0, false, SidecarLaunchState.Disabled, null, 0.0);
            Assert.AreEqual("SIDECAR", lights[Sidecar].Label);
            Assert.AreEqual("WATCHDOG", lights[Watchdog].Label);
            Assert.AreEqual("SOURCE", lights[Source].Label);
            Assert.AreEqual("MODEL", lights[Model].Label);
            Assert.AreEqual("TRACKING", lights[Tracking].Label);
        }

        [Test]
        public void StartingBeforeTheSupervisorHasWrittenAnythingIsAmberNotRed() {
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] lights = Eval(model, 1.0, SidecarLaunchState.Starting, null);
            Expect(lights[Sidecar], StatusLevel.Warn, "starting");
            Expect(lights[Watchdog], StatusLevel.Warn, "waiting for the supervisor");
        }

        [Test]
        public void WhileTheModelLoadsTheProcessIsUpButTheModelIsAmber() {
            SidecarStatusModel model = new SidecarStatusModel();
            SupervisorSnapshot snapshot = Snapshot("RUNNING", 0, 0, null, "loading RTMW3D ...", 1.0, 0);
            StatusLight[] lights = Eval(model, 5.0, SidecarLaunchState.Running, snapshot);
            Expect(lights[Sidecar], StatusLevel.Ok, "running");
            Expect(lights[Watchdog], StatusLevel.Ok, "watching");
            Expect(lights[Model], StatusLevel.Warn, "loading");
            Expect(lights[Tracking], StatusLevel.Off, "-");
        }

        [Test]
        public void AHealthySystemIsAllGreenOnceTheCountersHaveBeenSeenToMove() {
            SidecarStatusModel model = new SidecarStatusModel();
            // The FIRST sighting of a counter proves nothing (it could be a stalled sender's last line).
            StatusLight[] first = Eval(model, 10.0, SidecarLaunchState.Running, Running(100, 90, "Y"));
            Assert.AreNotEqual(StatusLevel.Ok, first[Model].Level, "one observation is not movement");
            Assert.AreNotEqual(StatusLevel.Ok, first[Tracking].Level);

            StatusLight[] lights = Eval(model, 12.0, SidecarLaunchState.Running, Running(160, 150, "Y"));
            Expect(lights[Sidecar], StatusLevel.Ok, "running");
            Expect(lights[Watchdog], StatusLevel.Ok, "watching");
            Expect(lights[Source], StatusLevel.Ok, "connected");
            Expect(lights[Model], StatusLevel.Ok, "running");
            Expect(lights[Tracking], StatusLevel.Ok, "person tracked");
        }

        [Test]
        public void JustAfterAttachingToAHealthySidecarTheModelSaysCheckingNotIdle() {
            // Unity attaches to a sidecar that has been running for an hour. One poll cannot tell moving from
            // frozen, and "idle: no new frames" there would be a false alarm; after the activity window with
            // the counters still frozen it must turn into the real thing.
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] first = Eval(model, 10.0, SidecarLaunchState.Running, Running(5000, 4000, "Y"));
            Expect(first[Model], StatusLevel.Warn, "checking");
            StatusLight[] soon = Eval(model, 13.0, SidecarLaunchState.Running, Running(5000, 4000, "Y"));
            Expect(soon[Model], StatusLevel.Warn, "checking");
            StatusLight[] later = Eval(model, 17.0, SidecarLaunchState.Running, Running(5000, 4000, "Y"));
            Expect(later[Model], StatusLevel.Warn, "idle");
        }

        [Test]
        public void FramesMovingWhileNothingIsSentMeansNobodyWasFound() {
            // The exact situation seen with a phone: the camera delivers, the model runs, no person is posed.
            SidecarStatusModel model = new SidecarStatusModel();
            Eval(model, 10.0, SidecarLaunchState.Running, Running(100, 0, "Y"));
            StatusLight[] lights = Eval(model, 12.0, SidecarLaunchState.Running, Running(160, 0, "Y"));
            Expect(lights[Source], StatusLevel.Ok, "connected");
            Expect(lights[Model], StatusLevel.Ok, "running");
            Expect(lights[Tracking], StatusLevel.Warn, "no person found");
        }

        [Test]
        public void ASentCounterThatStopsMovingMeansThePersonWasLost() {
            SidecarStatusModel model = new SidecarStatusModel();
            Eval(model, 10.0, SidecarLaunchState.Running, Running(100, 50, "Y"));
            Eval(model, 12.0, SidecarLaunchState.Running, Running(160, 100, "Y"));
            // Frames keep arriving, sent is frozen at 100 for longer than the activity window.
            StatusLight[] lights = Eval(model, 20.0, SidecarLaunchState.Running, Running(400, 100, "Y"));
            Expect(lights[Model], StatusLevel.Ok, "running");
            Expect(lights[Tracking], StatusLevel.Warn, "person lost");
        }

        [Test]
        public void APhoneThatIsNotConnectedIsAmberAtTheSourceAndQuietBeyondIt() {
            SidecarStatusModel model = new SidecarStatusModel();
            SupervisorSnapshot waiting = Snapshot("RUNNING", 0, 0, "n", "waiting for frames", 1.0, 0);
            StatusLight[] lights = Eval(model, 10.0, SidecarLaunchState.Running, waiting);
            Expect(lights[Source], StatusLevel.Warn, "waiting for the camera");
            Expect(lights[Model], StatusLevel.Warn, "waiting for frames");
            Expect(lights[Tracking], StatusLevel.Off, "-");
        }

        [Test]
        public void ASenderWhoseLogHasStoppedGrowingIsRed() {
            SidecarStatusModel model = new SidecarStatusModel();
            SupervisorSnapshot silent = Snapshot("RUNNING", 500, 400, "Y", string.Empty, 20.0, 0);
            StatusLight[] lights = Eval(model, 10.0, SidecarLaunchState.Running, silent);
            Expect(lights[Source], StatusLevel.Bad, "silent");
            Expect(lights[Model], StatusLevel.Bad, "silent");
            Assert.AreEqual(StatusLevel.Off, lights[Tracking].Level);
        }

        [Test]
        public void AFileOrOakSenderWithNoLiveFieldIsJudgedByItsFrames() {
            SidecarStatusModel model = new SidecarStatusModel();
            Eval(model, 10.0, SidecarLaunchState.Running, Running(100, 100, null));
            StatusLight[] lights = Eval(model, 12.0, SidecarLaunchState.Running, Running(160, 160, null));
            Expect(lights[Source], StatusLevel.Ok, "frames arriving");
        }

        [Test]
        public void ARestartThatResetsTheCountersStillCountsAsAlive() {
            SidecarStatusModel model = new SidecarStatusModel();
            Eval(model, 10.0, SidecarLaunchState.Running, Running(5000, 4000, "Y"));
            StatusLight[] lights = Eval(model, 12.0, SidecarLaunchState.Running, Running(40, 30, "Y"));
            Expect(lights[Model], StatusLevel.Ok, "running");
        }

        // ---------------------------------------------------------------- the process and the watchdog

        [Test]
        public void EveryLauncherStateHasItsOwnLight() {
            SidecarStatusModel model = new SidecarStatusModel();
            Expect(Eval(model, 1.0, SidecarLaunchState.Failed, null)[Sidecar], StatusLevel.Bad, "failed");
            Expect(Eval(model, 1.0, SidecarLaunchState.AttachedExternal, null)[Sidecar], StatusLevel.Info, "external");
            Expect(Eval(model, 1.0, SidecarLaunchState.Disabled, null)[Sidecar], StatusLevel.Off, "manual");
            Expect(Eval(model, 1.0, SidecarLaunchState.Stopped, null)[Sidecar], StatusLevel.Off, "stopped");
            Expect(Eval(model, 1.0, SidecarLaunchState.Starting, null)[Sidecar], StatusLevel.Warn, "starting");
            Expect(Eval(model, 1.0, SidecarLaunchState.Running, null)[Sidecar], StatusLevel.Ok, "running");
        }

        [Test]
        public void ASidecarStartedOutsideUnityIsShownAsExternalNotAsMissing() {
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] lights = model.Evaluate(5.0, false, SidecarLaunchState.Disabled,
                                                  Running(10, 10, "Y"), 1.0);
            Expect(lights[Sidecar], StatusLevel.Info, "outside Unity");
            Expect(lights[Watchdog], StatusLevel.Ok, "watching");
        }

        [Test]
        public void TheWatchdogReportsRestartsAndCrashLoops() {
            SidecarStatusModel model = new SidecarStatusModel();
            SupervisorSnapshot backoff = Snapshot("BACKOFF", 0, 0, null, string.Empty, 1.0, 3);
            backoff.HasLastExitCode = true;
            backoff.LastExitCode = 2;
            Expect(Eval(model, 1.0, SidecarLaunchState.Running, backoff)[Watchdog], StatusLevel.Warn, "last exit code 2");

            Expect(Eval(model, 1.0, SidecarLaunchState.Running,
                        Snapshot("FAILED_PERMANENT", 0, 0, null, string.Empty, 1.0, 5))[Watchdog],
                   StatusLevel.Bad, "crash loop");
            Expect(Eval(model, 1.0, SidecarLaunchState.Running,
                        Snapshot("STARTING", 0, 0, null, string.Empty, 1.0, 0))[Watchdog], StatusLevel.Warn, "starting");
            Expect(Eval(model, 1.0, SidecarLaunchState.Running,
                        Snapshot("RUNNING", 10, 10, "Y", string.Empty, 1.0, 2))[Watchdog], StatusLevel.Ok, "restarted 2x");
        }

        [Test]
        public void AStateFileThatStoppedBeingWrittenMeansTheSupervisorIsGone() {
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] dead = model.Evaluate(100.0, true, SidecarLaunchState.Running, Running(10, 10, "Y"), 30.0);
            Expect(dead[Watchdog], StatusLevel.Bad, "not responding");
            Assert.AreEqual(StatusLevel.Off, dead[Model].Level,
                            "a stale file's heartbeat must not be reported as a live model");

            // The same stale file with nothing of ours running is just last session's leftover.
            StatusLight[] leftover = model.Evaluate(100.0, true, SidecarLaunchState.Stopped, Running(10, 10, "Y"), 30.0);
            Expect(leftover[Watchdog], StatusLevel.Off, "no supervisor");
            foreach (StatusLight light in leftover) {
                Assert.AreEqual(StatusLevel.Off, light.Level, light.Label);
            }
        }

        [Test]
        public void AStoppedSupervisorSilencesTheDownstreamLights() {
            SidecarStatusModel model = new SidecarStatusModel();
            StatusLight[] lights = Eval(model, 10.0, SidecarLaunchState.Stopped,
                                        Snapshot("STOPPED", 100, 100, "Y", string.Empty, 1.0, 0));
            Expect(lights[Watchdog], StatusLevel.Off, "stopped");
            Assert.AreEqual(StatusLevel.Off, lights[Source].Level);
            Assert.AreEqual(StatusLevel.Off, lights[Model].Level);
            Assert.AreEqual(StatusLevel.Off, lights[Tracking].Level);
        }

        [Test]
        public void AKeyChangesWhenAndOnlyWhenTheMeaningChanges() {
            // The console logs a light once per Key; a key that wobbled would spam it, one that never changed
            // would hide a failure.
            SidecarStatusModel model = new SidecarStatusModel();
            Eval(model, 10.0, SidecarLaunchState.Running, Running(100, 100, "Y"));
            StatusLight[] a = Eval(model, 12.0, SidecarLaunchState.Running, Running(160, 160, "Y"));
            StatusLight[] b = Eval(model, 14.0, SidecarLaunchState.Running, Running(220, 220, "Y"));
            for (int i = 0; i < a.Length; i++) {
                Assert.AreEqual(a[i].Key, b[i].Key, a[i].Label + " must not change while nothing changed");
            }
            StatusLight[] c = Eval(model, 14.5, SidecarLaunchState.Failed, Running(220, 220, "Y"));
            Assert.AreNotEqual(a[Sidecar].Key, c[Sidecar].Key);
        }
    }
}
