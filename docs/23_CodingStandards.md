# Virtual Mirror
## Software Design Specification

Document ID: SDS-023  
Document Name: Coding Standards  
Version: 1.0  
Status: Active  

---

# 1. Authority

1. Parent project `AGENTS.md`  
2. `.editorconfig`  
3. This document  
4. Existing code patterns  

If conflict: **AGENTS.md wins**.

---

# 2. C# Style (strict)

| Rule | Required |
|------|----------|
| `var` | **Forbidden** — explicit types |
| `new()` target-typed | **Forbidden** |
| Expression-bodied members | **Forbidden** |
| Braces | K&R (`if (x) {`) |
| Modifier order | public → private → protected → internal → static → … |
| Fields | `[SerializeField] private` camelCase |
| Public API | PascalCase |

Example:

```csharp
[SerializeField] private float moveSpeed;

public int CurrentLoad { get; private set; }

public void Execute() {
    if (condition) {
        DoWork();
    } else {
        Handle();
    }
}
```

---

# 3. Unity Practices

- Thin MonoBehaviours  
- Composition over inheritance  
- No LINQ / alloc in hot paths  
- Cache references  
- Events/callbacks over polling where possible  
- Validate inputs; log failures — never silent fail  

---

# 4. Architecture Practices

- Depend on interfaces in `VirtualMirror.Core`  
- No cyclic asmdef references  
- One public type per file  
- Namespaces match folders  

---

# 5. Async

- Prefer non-blocking IO  
- Document chosen async library in ADR  
- Cancel on destroy / playmode exit  

---

# 6. Comments

- No narrating comments  
- Comment only non-obvious invariants / coordinate space assumptions  

---

# 7. Testing

- Math and filters: EditMode tests  
- Providers: fakes for PlayMode  

---

# 8. Git

- Commit only when user asks  
- Do not commit secrets, huge binaries, or personal VRM sets without license
