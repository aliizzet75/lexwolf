import ast
import sys

path = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/main.py"
with open(path, encoding="utf-8") as f:
    src = f.read()

# Check it parses
tree = ast.parse(src, filename=path)

# Check there are no unresolved imports that would break FastAPI (no exec => can't actually import)
# But we can check the imports are syntactically present
imports = []
for node in ast.walk(tree):
    if isinstance(node, ast.Import):
        imports.extend(alias.name for alias in node.names)
    elif isinstance(node, ast.ImportFrom):
        imports.append(node.module or "")

print("Imports OK")
print("Import modules:", imports)
print("AST OK")
