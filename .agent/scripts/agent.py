from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import unquote

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

AGENT_DIR = Path(__file__).resolve().parent.parent
ROOT = AGENT_DIR.parent
LOCAL = AGENT_DIR / ".local"
INDEX_DIR = AGENT_DIR / "indexes"
SCHEMA_DIR = AGENT_DIR / "schemas"
EVENTS_FILE = AGENT_DIR / "events" / "events.jsonl"
STATE_FILE = AGENT_DIR / "state.json"
ACTIVE_FILE = AGENT_DIR / "tasks" / "active.json"
HANDOFF_FILE = AGENT_DIR / "tasks" / "handoff.json"
UNITY_VERIFY_FILE = LOCAL / "unity-verify.json"
UNITY_VERIFY_EDITOR_FILE = LOCAL / "unity-verify-editor.json"
UNITY_VERIFY_LOG = LOCAL / "unity-verify.log"
UNITY_TEST_FILE = LOCAL / "unity-test.json"
UNITY_TEST_EDITOR_FILE = LOCAL / "unity-test-editor.json"
GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
CS_ERROR_RE = re.compile(r"\berror CS\d+\b")
CS_WARNING_RE = re.compile(r"\bwarning CS\d+\b")
COMPILE_INPUT_SUFFIXES = {".cs", ".asmdef", ".rsp"}
YAML_ASSET_SUFFIXES = {".unity", ".prefab", ".asset", ".mat", ".controller", ".playable", ".anim"}
HOOK_MARKER = "# managed-by: vindicator-agent-harness"

CATEGORY_BY_SUFFIX = {
    ".cs": "script",
    ".unity": "scene",
    ".prefab": "prefab",
    ".asset": "asset",
    ".mat": "material",
    ".controller": "animator-controller",
    ".anim": "animation",
    ".shader": "shader",
    ".inputactions": "input-actions",
    ".json": "config",
    ".txt": "text",
    ".md": "doc",
    ".png": "texture",
    ".jpg": "texture",
    ".jpeg": "texture",
    ".tga": "texture",
    ".psd": "texture",
    ".fbx": "model",
    ".obj": "model",
    ".wav": "audio",
    ".mp3": "audio",
    ".ogg": "audio",
}


class HarnessError(Exception):
    pass


def now_iso():
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def rel_path(path):
    return path.resolve().relative_to(ROOT).as_posix()


def read_json(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        raise HarnessError("missing file: %s" % path)
    except json.JSONDecodeError as exc:
        raise HarnessError("invalid JSON in %s: %s" % (path, exc))


def write_json(path, obj):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=str(path.parent), prefix=path.name + ".", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            json.dump(obj, f, ensure_ascii=False, indent=2)
            f.write("\n")
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def run_git(args):
    try:
        proc = subprocess.run(
            ["git"] + args,
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
        )
        return proc.returncode, proc.stdout.strip(), proc.stderr.strip()
    except (OSError, subprocess.TimeoutExpired) as exc:
        return 127, "", str(exc)


def git_info():
    head_code, head, _ = run_git(["rev-parse", "--short", "HEAD"])
    branch_code, branch, _ = run_git(["rev-parse", "--abbrev-ref", "HEAD"])
    status_code, status_out, _ = run_git(["-c", "core.quotepath=false", "status", "--porcelain"])
    lines = [line for line in status_out.splitlines() if line.strip()] if status_code == 0 else []
    ahead = behind = None
    up_code, up_out, _ = run_git(["rev-list", "--left-right", "--count", "@{u}...HEAD"])
    if up_code == 0 and up_out:
        parts = up_out.split()
        if len(parts) == 2:
            behind, ahead = int(parts[0]), int(parts[1])
    return {
        "head": head if head_code == 0 else None,
        "branch": branch if branch_code == 0 else None,
        "dirty": bool(lines),
        "status_lines": lines,
        "ahead": ahead,
        "behind": behind,
    }


class FileLock:
    def __init__(self, path, timeout=30.0, stale_after=600.0):
        self.path = Path(path)
        self.timeout = timeout
        self.stale_after = stale_after
        self.handle = None

    def __enter__(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        deadline = time.time() + self.timeout
        while True:
            try:
                self.handle = os.open(str(self.path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(self.handle, json.dumps({"pid": os.getpid(), "ts": time.time()}).encode("utf-8"))
                return self
            except FileExistsError:
                try:
                    age = time.time() - self.path.stat().st_mtime
                    if age > self.stale_after:
                        self.path.unlink()
                        continue
                except OSError:
                    pass
                if time.time() > deadline:
                    raise HarnessError("lock timeout: %s" % self.path)
                time.sleep(0.25)

    def __exit__(self, exc_type, exc, tb):
        if self.handle is not None:
            os.close(self.handle)
            self.handle = None
        try:
            self.path.unlink()
        except OSError:
            pass
        return False


def load_project():
    project = read_json(AGENT_DIR / "project.json")
    project.setdefault("index", {})
    project["index"].setdefault("exclude_rel_prefixes", [])
    project["index"].setdefault("file_roots", ["Assets"])
    project["index"].setdefault("root_files", [])
    project["index"].setdefault("signature_roots", project["index"]["file_roots"])
    return project


def is_excluded(rel, excludes):
    for prefix in excludes:
        if prefix.endswith("/"):
            if rel.startswith(prefix):
                return True
        elif rel == prefix or rel.startswith(prefix + "/"):
            return True
    return False


def iter_source_files(project):
    excludes = project["index"]["exclude_rel_prefixes"]
    roots = project["index"]["signature_roots"]
    root_files = project["index"]["root_files"]
    seen = set()
    for root in roots:
        base = ROOT / root
        if not base.exists():
            continue
        candidates = [base] if base.is_file() else sorted(base.rglob("*"))
        for path in candidates:
            if not path.is_file():
                continue
            try:
                rel = rel_path(path)
            except ValueError:
                continue
            if rel in seen or is_excluded(rel, excludes):
                continue
            seen.add(rel)
            yield path, rel
    for name in root_files:
        path = ROOT / name
        if path.is_file() and name not in seen:
            seen.add(name)
            yield path, name


def source_hash_info(project):
    entries = []
    digest = hashlib.sha256()
    for path, rel in iter_source_files(project):
        try:
            file_hash = sha256_file(path)
            size = path.stat().st_size
        except OSError as exc:
            file_hash = "unreadable:" + str(exc)
            size = 0
        entries.append({"path": rel, "size": size, "sha256": file_hash})
        digest.update(rel.encode("utf-8"))
        digest.update(b"\0")
        digest.update(file_hash.encode("utf-8"))
        digest.update(b"\n")
    return {
        "signature": "sha256:" + digest.hexdigest(),
        "file_count": len(entries),
        "entries": entries,
        "by_path": {e["path"]: e for e in entries},
    }


def light_signature(project):
    excludes = project["index"]["exclude_rel_prefixes"]
    roots = project["index"]["signature_roots"]
    root_files = project["index"]["root_files"]
    parts = []
    seen = set()
    for root in roots:
        base = ROOT / root
        if not base.exists():
            continue
        candidates = [base] if base.is_file() else sorted(base.rglob("*"))
        for path in candidates:
            if not path.is_file():
                continue
            try:
                rel = rel_path(path)
            except ValueError:
                continue
            if rel in seen or is_excluded(rel, excludes):
                continue
            seen.add(rel)
            try:
                st = path.stat()
                parts.append("%s\0%d\0%d" % (rel, st.st_size, st.st_mtime_ns))
            except OSError:
                parts.append(rel + "\0?\0?")
    for name in root_files:
        path = ROOT / name
        if path.is_file():
            st = path.stat()
            parts.append("%s\0%d\0%d" % (name, st.st_size, st.st_mtime_ns))
    return hashlib.sha256("\n".join(parts).encode("utf-8")).hexdigest()


def append_event(event_type, summary, details=None, actor="agent.py"):
    git = git_info()
    event = {
        "ts": now_iso(),
        "type": event_type,
        "actor": actor,
        "summary": summary,
        "details": details or {},
        "git_head": git["head"],
        "branch": git["branch"],
    }
    EVENTS_FILE.parent.mkdir(parents=True, exist_ok=True)
    try:
        with FileLock(LOCAL / "events.lock", timeout=10.0, stale_after=60.0):
            with open(EVENTS_FILE, "a", encoding="utf-8", newline="\n") as f:
                f.write(json.dumps(event, ensure_ascii=False) + "\n")
    except (OSError, HarnessError) as exc:
        print("warning: could not append event: %s" % exc, file=sys.stderr)
    return event


def read_events(limit=None):
    events = []
    if not EVENTS_FILE.exists():
        return events
    with open(EVENTS_FILE, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                events.append({"type": "invalid", "summary": "unparseable event line"})
    return events[-limit:] if limit else events


def category_for(rel):
    return CATEGORY_BY_SUFFIX.get(Path(rel).suffix.lower(), "other")


def meta_info(meta_path):
    try:
        head = Path(meta_path).read_text(encoding="utf-8", errors="replace")[:400]
    except OSError:
        return None, None
    guid_match = GUID_RE.search(head)
    importer_match = re.search(r"^\s*(\w+Importer):", head, re.MULTILINE)
    return (
        guid_match.group(1) if guid_match else None,
        importer_match.group(1) if importer_match else None,
    )


def build_files_index(source_info, project):
    excludes = project["index"]["exclude_rel_prefixes"]
    roots = project["index"]["file_roots"]
    root_files = project["index"]["root_files"]
    wanted = []
    for root in roots:
        base = ROOT / root
        if not base.exists():
            continue
        candidates = [base] if base.is_file() else sorted(base.rglob("*"))
        for path in candidates:
            if not path.is_file():
                continue
            try:
                rel = rel_path(path)
            except ValueError:
                continue
            if is_excluded(rel, excludes):
                continue
            wanted.append(rel)
    wanted.extend(name for name in root_files if (ROOT / name).is_file())
    entries = []
    for rel in sorted(set(wanted)):
        info = source_info["by_path"].get(rel)
        if not info or rel.endswith(".meta"):
            continue
        entries.append({
            "path": rel,
            "category": category_for(rel),
            "size": info["size"],
            "sha256": info["sha256"],
        })
    by_category = Counter(e["category"] for e in entries)
    return {
        "schema_version": 1,
        "generated_at": now_iso(),
        "count": len(entries),
        "by_category": dict(sorted(by_category.items())),
        "entries": entries,
    }


def scan_project_guids():
    guid_to_path = {}
    duplicates = []
    for meta in sorted((ROOT / "Assets").rglob("*.meta")):
        guid, _ = meta_info(meta)
        if not guid:
            continue
        asset = rel_path(Path(str(meta)[:-5]))
        if guid in guid_to_path and guid_to_path[guid] != asset:
            duplicates.append({"guid": guid, "paths": [guid_to_path[guid], asset]})
        guid_to_path[guid] = asset
    return guid_to_path, duplicates


def meta_pair_issues():
    missing, stray = [], []
    assets = ROOT / "Assets"
    if not assets.exists():
        return missing, stray
    for path in assets.rglob("*"):
        if path.is_dir():
            if not Path(str(path) + ".meta").exists():
                missing.append(rel_path(path) + "/")
        elif path.suffix == ".meta":
            if not Path(str(path)[:-5]).exists():
                stray.append(rel_path(path))
        elif not Path(str(path) + ".meta").exists():
            missing.append(rel_path(path))
    return sorted(missing), sorted(stray)


def collect_yaml_refs():
    refs = defaultdict(set)
    roots = [ROOT / "Assets", ROOT / "ProjectSettings"]
    for base in roots:
        if not base.exists():
            continue
        for path in base.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in YAML_ASSET_SUFFIXES:
                continue
            try:
                text = path.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            rel = rel_path(path)
            for guid in set(GUID_RE.findall(text)):
                refs[guid].add(rel)
    return refs


def build_assets_index(source_info):
    guid_to_path, guid_dupes = scan_project_guids()
    refs = collect_yaml_refs()
    missing_meta, stray_meta = meta_pair_issues()
    assets = []
    hash_groups = defaultdict(list)
    for path in sorted((ROOT / "Assets").rglob("*")):
        if not path.is_file() or path.suffix == ".meta":
            continue
        rel = rel_path(path)
        if is_excluded(rel, ["Assets/Screenshots/"]):
            continue
        guid, importer = meta_info(Path(str(path) + ".meta"))
        info = source_info["by_path"].get(rel)
        if info is None:
            try:
                info = {"size": path.stat().st_size, "sha256": sha256_file(path)}
            except OSError:
                info = {"size": 0, "sha256": ""}
        incoming = sorted(refs.get(guid, set()) - {rel}) if guid else []
        assets.append({
            "path": rel,
            "category": category_for(rel),
            "ext": path.suffix.lower(),
            "size": info["size"],
            "sha256": info["sha256"],
            "guid": guid,
            "importer": importer,
            "referenced_by": incoming[:50],
            "reference_count": len(incoming),
        })
        if info["sha256"] and not rel.endswith(".cs"):
            hash_groups[info["sha256"]].append(rel)
    duplicates = [
        {"sha256": h, "paths": sorted(paths)}
        for h, paths in sorted(hash_groups.items())
        if len(paths) > 1
    ]
    unused = []
    for asset in assets:
        rel = asset["path"]
        if asset["reference_count"] > 0 or not asset["guid"]:
            continue
        if asset["category"] in ("scene", "script"):
            continue
        if rel.startswith("Assets/Settings/") or rel.startswith("Assets/TutorialInfo/"):
            continue
        unused.append({"path": rel, "confidence": "low", "reason": "项目内 YAML 未发现 GUID 引用；运行时/代码加载仍可能使用"})
    unresolved = sorted(g for g in refs if g not in guid_to_path and not g.startswith("0000000000000000"))
    by_category = Counter(a["category"] for a in assets)
    return {
        "schema_version": 1,
        "generated_at": now_iso(),
        "count": len(assets),
        "by_category": dict(sorted(by_category.items())),
        "assets": assets,
        "stats": {
            "guid_count": len(guid_to_path),
            "guid_duplicates": guid_dupes,
            "meta_missing": missing_meta,
            "meta_stray": stray_meta,
            "unresolved_reference_guids": len(unresolved),
            "unresolved_reference_sample": unresolved[:20],
            "duplicate_hash_groups": len(duplicates),
            "unused_candidates": len(unused),
        },
        "duplicates": duplicates,
        "unused_candidates": unused,
    }


TYPE_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:(?:public|internal|protected|private)\s+)?"
    r"(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*"
    r"(class|struct|interface|enum)\s+(\w+)"
    r"(?:\s*<[^>{]*>)?"
    r"(?:\s*:\s*([^\{\n]+))?",
    re.MULTILINE,
)
METHOD_RE = re.compile(
    r"^\s*public\s+"
    r"(?:(?:static|virtual|override|sealed|abstract|async|new|extern|unsafe|partial)\s+)*"
    r"(?:[\w\.<>\[\],\?\s]+?)\s+(\w+)\s*\(",
    re.MULTILINE,
)
MENU_RE = re.compile(r'MenuItem\("([^"]+)"')
USING_RE = re.compile(r"^\s*using\s+([\w\.]+)\s*;", re.MULTILINE)
NAMESPACE_RE = re.compile(r"^\s*namespace\s+([\w\.]+)", re.MULTILINE)


def build_code_map():
    files = []
    texts = {}
    for path in sorted((ROOT / "Assets").rglob("*.cs")):
        rel = rel_path(path)
        text = path.read_text(encoding="utf-8", errors="replace")
        texts[rel] = text
        files.append({
            "path": rel,
            "lines": text.count("\n") + 1,
            "editor": "/Editor/" in "/" + rel or rel.endswith("/Editor"),
            "usings": sorted(set(USING_RE.findall(text))),
            "namespaces": sorted(set(NAMESPACE_RE.findall(text))),
            "menu_items": MENU_RE.findall(text),
        })
    type_names = []
    types = []
    for path in sorted((ROOT / "Assets").rglob("*.cs")):
        rel = rel_path(path)
        text = texts[rel]
        for match in TYPE_RE.finditer(text):
            kind, name, bases = match.group(1), match.group(2), (match.group(3) or "").strip()
            line = text.count("\n", 0, match.start()) + 1
            base_list = [b.strip() for b in bases.split(",") if b.strip()]
            types.append({
                "name": name,
                "kind": kind,
                "file": rel,
                "line": line,
                "bases": base_list,
                "editor": "/Editor/" in "/" + rel,
                "behaviour": any(b.split("<")[0] in ("MonoBehaviour", "ScriptableObject", "EditorWindow", "Editor", "StateMachineBehaviour") for b in base_list),
            })
            type_names.append(name)
    methods = {}
    for rel, text in texts.items():
        names = []
        for name in METHOD_RE.findall(text):
            if name in ("if", "for", "while", "switch", "using", "return", "new", "catch", "lock"):
                continue
            names.append(name)
        methods[rel] = sorted(set(names))
    references = {}
    for name in sorted(set(type_names)):
        pattern = re.compile(r"\b" + re.escape(name) + r"\b")
        hits = []
        count = 0
        for rel, text in texts.items():
            found = len(pattern.findall(text))
            if found:
                count += found
                if not any(t["name"] == name and t["file"] == rel for t in types):
                    hits.append(rel)
        references[name] = {"count": count, "files": sorted(hits)[:20]}
    return {
        "schema_version": 1,
        "generated_at": now_iso(),
        "stats": {
            "code_files": len(files),
            "types": len(types),
            "behaviours": sum(1 for t in types if t["behaviour"]),
            "editor_scripts": sum(1 for f in files if f["editor"]),
        },
        "files": files,
        "types": sorted(types, key=lambda t: (t["file"], t["line"])),
        "public_methods": methods,
        "type_references": references,
        "source": "heuristic regex parse; not a compiler",
    }


def build_dependencies(code_map):
    manifest = {}
    lock = {}
    if (ROOT / "Packages" / "manifest.json").exists():
        manifest = read_json(ROOT / "Packages" / "manifest.json")
    if (ROOT / "Packages" / "packages-lock.json").exists():
        lock = read_json(ROOT / "Packages" / "packages-lock.json")
    direct = []
    for name, requested in sorted(manifest.get("dependencies", {}).items()):
        locked = lock.get("dependencies", {}).get(name, {})
        direct.append({
            "name": name,
            "requested": requested,
            "resolved": locked.get("version"),
            "source": locked.get("source"),
            "hash": locked.get("hash"),
            "depth": locked.get("depth"),
        })
    unity_version = None
    version_file = ROOT / "ProjectSettings" / "ProjectVersion.txt"
    if version_file.exists():
        match = re.search(r"m_EditorVersion:\s*(\S+)", version_file.read_text(encoding="utf-8", errors="replace"))
        unity_version = match.group(1) if match else None
    test_files = []
    for path in (ROOT / "Assets").rglob("*"):
        if not path.is_file():
            continue
        rel = rel_path(path)
        if "/Tests/" in rel or path.name.endswith(("Tests.cs", "Test.cs")):
            test_files.append(rel)
    return {
        "schema_version": 1,
        "generated_at": now_iso(),
        "unity_version": unity_version,
        "package_count": len(direct),
        "direct_packages": direct,
        "asmdefs": sorted(rel_path(p) for p in (ROOT / "Assets").rglob("*.asmdef")),
        "csproj": sorted(rel_path(p) for p in ROOT.glob("*.csproj")),
        "test_files": sorted(test_files),
        "test_framework_installed": any(d["name"] == "com.unity.test-framework" for d in direct),
        "code": code_map["stats"],
    }


def rebuild_indexes(record_event=True):
    project = load_project()
    LOCAL.mkdir(parents=True, exist_ok=True)
    with FileLock(LOCAL / "index.lock", timeout=60.0, stale_after=600.0):
        INDEX_DIR.mkdir(parents=True, exist_ok=True)
        source_info = source_hash_info(project)
        files_index = build_files_index(source_info, project)
        assets_index = build_assets_index(source_info)
        code_map = build_code_map()
        dependencies = build_dependencies(code_map)
        generated_at = now_iso()
        payloads = {
            "files.json": files_index,
            "assets.json": assets_index,
            "code-map.json": code_map,
            "dependencies.json": dependencies,
        }
        for name, payload in payloads.items():
            write_json(INDEX_DIR / name, payload)
        git = git_info()
        manifest = {
            "schema_version": 1,
            "generated_at": generated_at,
            "signature": source_info["signature"],
            "git_head": git["head"],
            "branch": git["branch"],
            "dirty": git["dirty"],
            "counts": {
                "source_files": source_info["file_count"],
                "files": files_index["count"],
                "assets": assets_index["count"],
                "code_files": code_map["stats"]["code_files"],
                "types": code_map["stats"]["types"],
                "packages": dependencies["package_count"],
                "unused_candidates": assets_index["stats"]["unused_candidates"],
            },
            "files": {
                name: sha256_file(INDEX_DIR / name) for name in payloads
            },
        }
        write_json(INDEX_DIR / "manifest.json", manifest)
        state = load_state(project, create=True)
        state["index"] = {
            "generated_at": generated_at,
            "signature": source_info["signature"],
            "stale": False,
        }
        state["updated_at"] = generated_at
        write_json(STATE_FILE, state)
        runtime = read_local_runtime()
        runtime["last_index"] = generated_at
        runtime["last_index_signature"] = source_info["signature"]
        write_json(LOCAL / "runtime.json", runtime)
        if record_event:
            append_event(
                "index.rebuild",
                "索引重建：%d 个源文件，%d 个资产" % (files_index["count"], assets_index["count"]),
                {"signature": source_info["signature"], "counts": manifest["counts"]},
            )
        return manifest


def index_status(project=None):
    project = project or load_project()
    manifest_path = INDEX_DIR / "manifest.json"
    required = [INDEX_DIR / name for name in ("files.json", "assets.json", "code-map.json", "dependencies.json")]
    if not manifest_path.exists():
        return True, "索引不存在", None
    if not all(path.exists() for path in required):
        return True, "索引文件不完整", None
    try:
        manifest = read_json(manifest_path)
    except HarnessError as exc:
        return True, str(exc), None
    info = source_hash_info(project)
    if manifest.get("signature") != info["signature"]:
        return True, "工作树内容变化，索引过期", manifest
    return False, "索引新鲜", manifest


def read_local_runtime():
    path = LOCAL / "runtime.json"
    if not path.exists():
        return {}
    try:
        return read_json(path)
    except HarnessError:
        return {}


def local_env():
    path = LOCAL / "env.json"
    if not path.exists():
        return {}
    try:
        data = read_json(path)
    except HarnessError:
        return {}
    return data if isinstance(data, dict) else {}


def editor_path(project=None):
    project = project or load_project()
    return local_env().get("unity_editor") or project.get("engine", {}).get("editor")


def default_state(project=None):
    project = project or load_project()
    git = git_info()
    return {
        "schema_version": 1,
        "project": project.get("name"),
        "updated_at": now_iso(),
        "branch": git["branch"],
        "last_verified_commit": git["head"],
        "working_tree": "dirty" if git["dirty"] else "clean",
        "working_tree_summary": git["status_lines"][:50],
        "active_task_id": None,
        "task_status": "idle",
        "blockers": [],
        "next_action": "本机尚无交接记录；跨机进度以 tasks/backlog.json 与 git log 为准。",
        "index": {"generated_at": None, "signature": None, "stale": True},
    }


def default_active():
    return {
        "schema_version": 1,
        "updated_at": now_iso(),
        "note": "本机任务状态；跨机共享的任务池在 tasks/backlog.json。",
        "task": None,
    }


def load_state(project=None, create=False):
    if STATE_FILE.exists():
        try:
            return read_json(STATE_FILE)
        except HarnessError:
            pass
    state = default_state(project)
    if create:
        write_json(STATE_FILE, state)
    return state


def load_active(create=False):
    if ACTIVE_FILE.exists():
        try:
            return read_json(ACTIVE_FILE)
        except HarnessError:
            pass
    active = default_active()
    if create:
        write_json(ACTIVE_FILE, active)
    return active


def watcher_status():
    path = LOCAL / "watch.json"
    if not path.exists():
        return {"running": False, "detail": "未启动（无实时监控）"}
    try:
        info = read_json(path)
    except HarnessError:
        return {"running": False, "detail": "watch.json 无法解析"}
    heartbeat = info.get("heartbeat")
    if not heartbeat:
        return {"running": False, "detail": "无心跳", "info": info}
    try:
        age = time.time() - datetime.strptime(heartbeat, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc).timestamp()
    except ValueError:
        age = 10 ** 9
    interval = float(info.get("interval") or 3)
    running = age <= max(10.0, interval * 3)
    return {
        "running": running,
        "age_seconds": round(age, 1),
        "stale": bool(info.get("stale")),
        "detail": ("运行中，心跳 %.1fs 前" % age) if running else ("心跳已过期（%.0fs 前）" % age),
        "info": info,
    }


def cmd_index(args):
    manifest = rebuild_indexes(record_event=True)
    if not args.quiet:
        print("索引已重建：%s" % (INDEX_DIR.relative_to(ROOT).as_posix()))
        for key, value in manifest["counts"].items():
            print("  %-18s %s" % (key, value))
        print("  signature          %s" % manifest["signature"])
    return 0


def cmd_status(args):
    project = load_project()
    git = git_info()
    state = load_state(project)
    active = load_active()
    stale, reason, manifest = index_status(project)
    watch = watcher_status()
    last_validate = None
    validate_path = LOCAL / "last_validate.json"
    if validate_path.exists():
        try:
            last_validate = read_json(validate_path)
        except HarnessError:
            last_validate = None
    verify = unity_verify_summary(project)
    tests = unity_test_summary(project)
    payload = {
        "git": git,
        "state": state,
        "active": active,
        "index": {"stale": stale, "reason": reason, "manifest": manifest},
        "watcher": watch,
        "last_validate": last_validate,
        "unity_verify": verify,
        "unity_test": tests,
    }
    if args.json:
        print(json.dumps(payload, ensure_ascii=False, indent=2))
        return 0
    print("== Vindicator harness status ==")
    print("分支: %s  HEAD: %s  %s" % (git["branch"], git["head"], "有未提交改动" if git["dirty"] else "工作树干净"))
    if git["ahead"] is not None:
        print("上游: ahead %s / behind %s" % (git["ahead"], git["behind"]))
    if git["status_lines"]:
        for line in git["status_lines"][:50]:
            print("  " + line)
        if len(git["status_lines"]) > 50:
            print("  ... 其余 %d 条" % (len(git["status_lines"]) - 50))
    print("索引: %s（%s）" % ("过期" if stale else "新鲜", reason))
    if manifest:
        print("  生成于 %s；%s" % (manifest.get("generated_at"), ", ".join("%s=%s" % (k, v) for k, v in manifest.get("counts", {}).items())))
    print("实时感知: %s" % watch["detail"])
    print("编译验证: %s" % format_unity_verify(verify))
    print("测试验证: %s" % format_unity_test(tests))
    task = active.get("task")
    print("当前任务: %s" % (("%s (%s)" % (task.get("title"), task.get("status"))) if task else "无"))
    print("下一步: %s" % state.get("next_action"))
    if state.get("blockers"):
        print("阻塞: %s" % "; ".join(state["blockers"]))
    if last_validate:
        print("上次校验: %s（errors=%d warnings=%d）" % (
            last_validate.get("generated_at"), len(last_validate.get("errors", [])), len(last_validate.get("warnings", []))))
    return 0


def load_schema(name):
    return read_json(SCHEMA_DIR / name)


def validate_json_document(doc, schema, label, errors, warnings, checks):
    try:
        import jsonschema
    except ImportError:
        if not isinstance(doc, dict):
            errors.append("%s: 顶层不是对象" % label)
        checks.append({"name": "schema:" + label, "status": "skip", "detail": "jsonschema 未安装"})
        return
    validator = jsonschema.Draft7Validator(schema)
    problems = sorted(validator.iter_errors(doc), key=lambda e: list(e.path))
    if problems:
        for problem in problems[:5]:
            where = "/".join(str(p) for p in problem.path) or "<root>"
            errors.append("%s: %s: %s" % (label, where, problem.message))
        checks.append({"name": "schema:" + label, "status": "error", "detail": "%d 个 schema 问题" % len(problems)})
    else:
        checks.append({"name": "schema:" + label, "status": "pass", "detail": "符合 schema"})


def package_guid_map():
    cache_path = LOCAL / "package-guid-cache.json"
    lock_path = ROOT / "Packages" / "packages-lock.json"
    cache_key = sha256_file(lock_path) if lock_path.exists() else ""
    if cache_path.exists():
        try:
            cache = read_json(cache_path)
            if cache.get("key") == cache_key and cache.get("entries"):
                return cache["entries"], "cache"
        except HarnessError:
            pass
    cache_dir = ROOT / "Library" / "PackageCache"
    if not cache_dir.exists():
        return {}, "missing"
    entries = {}
    for meta in cache_dir.rglob("*.cs.meta"):
        guid, _ = meta_info(meta)
        if not guid:
            continue
        try:
            entries[guid] = Path(str(meta)[:-5]).relative_to(ROOT).as_posix()
        except ValueError:
            continue
    write_json(cache_path, {"key": cache_key, "entries": entries})
    return entries, "scan"


def find_missing_scripts(project_guids):
    package_guids, mode = package_guid_map()
    known = set(project_guids) | set(package_guids)
    missing = defaultdict(list)
    for path in sorted((ROOT / "Assets").rglob("*")):
        if path.suffix.lower() not in (".unity", ".prefab"):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in re.finditer(r"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}", text):
            guid = match.group(1)
            if guid not in known:
                missing[guid].append(rel_path(path))
    return missing, mode


def parse_doc_links(md_files):
    broken = []
    pattern = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
    for path in md_files:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for target in pattern.findall(text):
            target = target.strip()
            if not target or target.startswith(("#", "http://", "https://", "mailto:")):
                continue
            target = unquote(target.split("#", 1)[0])
            if not target:
                continue
            resolved = (path.parent / target).resolve()
            if not resolved.exists():
                broken.append({"file": rel_path(path), "target": target})
    return broken


def validate_all(project=None, strict=False, write_local=True):
    project = project or load_project()
    errors, warnings, checks = [], [], []

    def add(name, status, detail):
        checks.append({"name": name, "status": status, "detail": detail})

    docs = {
        "project": (AGENT_DIR / "project.json", "project.schema.json"),
        "backlog": (AGENT_DIR / "tasks" / "backlog.json", "backlog.schema.json"),
    }
    parsed = {}
    for label, (path, schema_name) in docs.items():
        try:
            parsed[label] = read_json(path)
            validate_json_document(parsed[label], load_schema(schema_name), label, errors, warnings, checks)
        except HarnessError as exc:
            errors.append(str(exc))
            add("schema:" + label, "error", str(exc))

    # Machine-local files: absent on a fresh clone or another machine. Their absence is
    # normal; session-start/checkpoint create them from defaults.
    local_docs = {
        "state": (STATE_FILE, "state.schema.json"),
        "active": (ACTIVE_FILE, "active.schema.json"),
    }
    for label, (path, schema_name) in local_docs.items():
        if not path.exists():
            add("schema:" + label, "skip", "本机状态文件不存在（新克隆/多机正常）")
            continue
        try:
            parsed[label] = read_json(path)
            validate_json_document(parsed[label], load_schema(schema_name), label, errors, warnings, checks)
        except HarnessError as exc:
            errors.append(str(exc))
            add("schema:" + label, "error", str(exc))

    if HANDOFF_FILE.exists():
        try:
            validate_json_document(read_json(HANDOFF_FILE), load_schema("handoff.schema.json"), "handoff", errors, warnings, checks)
        except HarnessError as exc:
            errors.append(str(exc))
            add("schema:handoff", "error", str(exc))
    else:
        add("schema:handoff", "skip", "本机尚无交接记录（checkpoint 会创建）")

    manifest_path = INDEX_DIR / "manifest.json"
    if manifest_path.exists():
        try:
            validate_json_document(read_json(manifest_path), load_schema("index-manifest.schema.json"), "index-manifest", errors, warnings, checks)
        except HarnessError as exc:
            errors.append(str(exc))
            add("schema:index-manifest", "error", str(exc))
    else:
        add("schema:index-manifest", "skip", "索引尚未生成")

    event_errors = []
    events = []
    if EVENTS_FILE.exists():
        with open(EVENTS_FILE, "r", encoding="utf-8", errors="replace") as f:
            for number, line in enumerate(f, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                    events.append(event)
                except json.JSONDecodeError as exc:
                    event_errors.append("第 %d 行: %s" % (number, exc))
                    continue
                try:
                    import jsonschema
                    problem = next(jsonschema.Draft7Validator(load_schema("event.schema.json")).iter_errors(event), None)
                    if problem:
                        event_errors.append("第 %d 行: %s" % (number, problem.message))
                except ImportError:
                    pass
    if event_errors:
        errors.extend("events.jsonl " + item for item in event_errors[:5])
        add("events", "error", "%d 行无效" % len(event_errors))
    else:
        add("events", "pass", "%d 条事件" % len(events))

    path_errors = []
    for key, value in project.get("paths", {}).items():
        values = value if isinstance(value, list) else [value]
        for item in values:
            if isinstance(item, str) and not (ROOT / item).exists():
                path_errors.append("%s -> %s" % (key, item))
    if path_errors:
        errors.extend("缺失路径: " + item for item in path_errors)
        add("paths", "error", "; ".join(path_errors[:5]))
    else:
        add("paths", "pass", "project.json 引用路径均存在")

    if parsed.get("backlog") and parsed.get("state"):
        tasks = parsed["backlog"].get("tasks", [])
        ids = [t.get("id") for t in tasks]
        duplicates = [i for i, c in Counter(ids).items() if c > 1]
        active_id = parsed["state"].get("active_task_id")
        task_problems = []
        if duplicates:
            task_problems.append("重复任务 id: %s" % ", ".join(duplicates))
        if active_id and active_id not in ids:
            task_problems.append("active_task_id 不在 backlog: %s" % active_id)
        for task in tasks:
            if not task.get("acceptance_criteria"):
                task_problems.append("%s 缺少验收标准" % task.get("id"))
            source = task.get("source", "")
            source_path = source.split("#", 1)[0]
            if source_path and not (ROOT / source_path).exists():
                task_problems.append("%s 的来源不存在: %s" % (task.get("id"), source_path))
        active_doc = parsed.get("active", {})
        if active_doc.get("task") and not active_doc["task"].get("acceptance_criteria"):
            task_problems.append("active task 缺少验收标准")
        if task_problems:
            warnings.extend(task_problems)
            add("tasks", "warn", "%d 个问题" % len(task_problems))
        else:
            add("tasks", "pass", "%d 个任务，引用一致" % len(tasks))

    stale, reason, manifest = index_status(project)
    if stale:
        warnings.append("索引: " + reason)
        add("index-freshness", "warn", reason)
    else:
        add("index-freshness", "pass", "签名一致")

    md_files = [ROOT / "AGENTS.md", ROOT / "CLAUDE.md"]
    md_files.extend(sorted((AGENT_DIR / "memory").rglob("*.md")))
    md_files.extend(sorted((AGENT_DIR / "reports").glob("*.md")))
    md_files.append(AGENT_DIR / "indexes" / "README.md")
    broken = parse_doc_links(md_files)
    if broken:
        errors.extend("文档链接失效: %s -> %s" % (b["file"], b["target"]) for b in broken[:10])
        add("doc-links", "error", "%d 个链接失效" % len(broken))
    else:
        add("doc-links", "pass", "本地 Markdown 链接有效")

    ignore_code, _, _ = run_git(["check-ignore", "-q", ".agent"])
    if ignore_code == 0:
        errors.append(".agent/ 被 Git 忽略，harness 无法跨会话持久化")
        add("git-ignore", "error", ".agent/ 被忽略")
    else:
        add("git-ignore", "pass", ".agent/ 已跟踪")
    lib_code, _, _ = run_git(["check-ignore", "-q", "Library"])
    if lib_code != 0:
        warnings.append("Library/ 未被 Git 忽略")
        add("git-ignore-generated", "warn", "Library/ 未被忽略")
    else:
        add("git-ignore-generated", "pass", "Unity 生成目录已忽略")

    local_state_paths = [
        ".agent/state.json",
        ".agent/tasks/active.json",
        ".agent/tasks/handoff.json",
        ".agent/events/events.jsonl",
    ]
    unignored = [path for path in local_state_paths if run_git(["check-ignore", "-q", path])[0] != 0]
    if unignored:
        warnings.append("本机状态文件未被忽略（会与他人的机器状态冲突）: " + ", ".join(unignored))
        add("git-ignore-local", "warn", "%d 个本机状态路径未忽略" % len(unignored))
    else:
        add("git-ignore-local", "pass", "本机状态文件已忽略（不随仓库共享）")

    guid_to_path, guid_dupes = scan_project_guids()
    if guid_dupes:
        errors.extend("GUID 重复: %s" % ", ".join(d["guid"] for d in guid_dupes[:5]))
        add("guid-uniqueness", "error", "%d 个重复 GUID" % len(guid_dupes))
    else:
        add("guid-uniqueness", "pass", "%d 个项目 GUID" % len(guid_to_path))

    missing_meta, stray_meta = meta_pair_issues()
    if missing_meta:
        warnings.extend("缺少 .meta: " + path for path in missing_meta[:20])
        add("meta-pairs", "warn", "%d 个文件缺 .meta，%d 个孤立 .meta" % (len(missing_meta), len(stray_meta)))
    elif stray_meta:
        warnings.extend("孤立 .meta: " + path for path in stray_meta[:20])
        add("meta-pairs", "warn", "%d 个孤立 .meta" % len(stray_meta))
    else:
        add("meta-pairs", "pass", "资产与 .meta 一一对应")

    missing_scripts, package_mode = find_missing_scripts(guid_to_path)
    if package_mode == "missing":
        add("missing-scripts", "skip", "Library/PackageCache 不存在，无法解析包内脚本 GUID")
    elif missing_scripts:
        for guid, files in list(missing_scripts.items())[:5]:
            errors.append("缺失脚本 GUID %s 被引用: %s" % (guid, ", ".join(sorted(set(files))[:3])))
        add("missing-scripts", "error", "%d 个缺失脚本 GUID" % len(missing_scripts))
    else:
        add("missing-scripts", "pass", "场景/预制体未发现缺失脚本")

    try:
        assets_index = read_json(INDEX_DIR / "assets.json") if (INDEX_DIR / "assets.json").exists() else None
    except HarnessError:
        assets_index = None
    if assets_index:
        duplicates = assets_index.get("duplicates", [])
        if duplicates:
            warnings.append("存在 %d 组内容完全相同的资产（仅提示，不判定冗余）" % len(duplicates))
            add("duplicate-assets", "warn", "%d 组重复内容" % len(duplicates))
        else:
            add("duplicate-assets", "pass", "无重复内容资产")
    else:
        add("duplicate-assets", "skip", "assets.json 不存在")

    verify = unity_verify_summary(project)
    if not verify.get("present"):
        evidence = verify.get("editor_evidence") or {}
        if evidence.get("unadopted"):
            warnings.append("存在未采纳的编辑器编译证据：python .agent/scripts/agent.py unity-verify --adopt-editor")
            add("unity-verify", "skip", "有未采纳的编辑器证据")
        else:
            add("unity-verify", "skip", "尚无编译验证记录（运行 unity-verify）")
    else:
        result = verify.get("result")
        if result in ("fail", "error", "timeout"):
            detail = "%s @ %s" % (result, verify.get("verified_at"))
            first = (verify.get("compile_errors") or [{}])[0]
            first_text = first.get("text") or first.get("message")
            if first_text:
                detail += "；" + str(first_text)[:160]
            errors.append("Unity 编译验证未通过：" + detail)
            add("unity-verify", "error", detail)
        elif verify.get("stale"):
            warnings.append("Unity 编译验证结果已过期：验证后编译相关文件有变化，请重新运行 unity-verify")
            add("unity-verify", "warn", "编译结果过期")
        else:
            add("unity-verify", "pass", "%s（%s，%s）" % (result, verify.get("mode"), verify.get("verified_at")))

    tests = unity_test_summary(project)
    if not tests.get("present"):
        evidence = tests.get("editor_evidence") or {}
        if evidence.get("unadopted"):
            warnings.append("存在未采纳的编辑器测试证据：python .agent/scripts/agent.py unity-test --adopt-editor")
            add("unity-test", "skip", "有未采纳的编辑器测试证据")
        else:
            add("unity-test", "skip", "尚无 EditMode 测试记录（编辑器菜单 Tools/Harness/Run EditMode Tests）")
    else:
        result = tests.get("result")
        if result == "fail":
            first = (tests.get("failures") or [{}])[0]
            detail = "fail @ %s；%s: %s" % (
                tests.get("verified_at"), first.get("name"), str(first.get("message"))[:160])
            errors.append("EditMode 测试未通过：" + detail)
            add("unity-test", "error", detail)
        elif result == "empty":
            warnings.append("EditMode 测试记录为 empty（没有可运行的测试）")
            add("unity-test", "warn", "empty（0 个测试）")
        elif tests.get("stale"):
            warnings.append("EditMode 测试结果已过期：测试后代码或包有变化，请重新运行")
            add("unity-test", "warn", "测试结果过期")
        else:
            add("unity-test", "pass", "%s（%s，%s；total=%s failed=%s）" % (
                result, tests.get("test_mode"), tests.get("verified_at"),
                tests.get("total"), tests.get("failed")))

    if parsed.get("state"):
        state = parsed["state"]
        git = git_info()
        state_problems = []
        if state.get("branch") != git["branch"]:
            state_problems.append("state 分支 %s != 当前 %s" % (state.get("branch"), git["branch"]))
        if state.get("last_verified_commit") != git["head"]:
            state_problems.append("state 记录提交 %s != HEAD %s" % (state.get("last_verified_commit"), git["head"]))
        if state_problems:
            warnings.extend(state_problems)
            add("state-git", "warn", "; ".join(state_problems))
        else:
            add("state-git", "pass", "state 与 Git 一致")

    sensitive = []
    for pattern in (".env", "*.pem", "id_rsa", "id_ed25519"):
        for path in ROOT.glob(pattern):
            if path.is_file():
                sensitive.append(rel_path(path))
    if sensitive:
        warnings.append("根目录存在敏感文件候选: " + ", ".join(sensitive))
        add("sensitive-files", "warn", ", ".join(sensitive))
    else:
        add("sensitive-files", "pass", "根目录未发现常见敏感文件")

    result = {
        "schema_version": 1,
        "generated_at": now_iso(),
        "ok": not errors,
        "strict": strict,
        "errors": errors,
        "warnings": warnings,
        "checks": checks,
        "index": {"stale": stale, "reason": reason},
    }
    if write_local:
        LOCAL.mkdir(parents=True, exist_ok=True)
        write_json(LOCAL / "last_validate.json", result)
        runtime = read_local_runtime()
        runtime["last_validate"] = result["generated_at"]
        write_json(LOCAL / "runtime.json", runtime)
    if errors:
        append_event("validate.failed", "校验失败：%d error / %d warning" % (len(errors), len(warnings)),
                     {"errors": errors[:10], "warnings": warnings[:10]})
    return result


def cmd_validate(args):
    result = validate_all(strict=args.strict)
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print("== 校验结果 ==")
        for check in result["checks"]:
            print("  [%-5s] %-24s %s" % (check["status"].upper(), check["name"], check["detail"]))
        if result["warnings"]:
            print("\n警告 (%d):" % len(result["warnings"]))
            for item in result["warnings"][:30]:
                print("  - " + item)
        if result["errors"]:
            print("\n错误 (%d):" % len(result["errors"]))
            for item in result["errors"][:30]:
                print("  - " + item)
        print("\n结论: %s" % ("通过" if result["ok"] and not (args.strict and result["warnings"]) else "未通过"))
    return 0 if result["ok"] and not (args.strict and result["warnings"]) else 1


def sync_state_from_git(project=None):
    state = load_state(project, create=True)
    git = git_info()
    updated = dict(state)
    updated["branch"] = git["branch"]
    updated["last_verified_commit"] = git["head"]
    updated["working_tree"] = "dirty" if git["dirty"] else "clean"
    updated["working_tree_summary"] = git["status_lines"][:50]
    changed = any(updated.get(key) != state.get(key) for key in ("branch", "last_verified_commit", "working_tree", "working_tree_summary"))
    if changed:
        updated["updated_at"] = now_iso()
        write_json(STATE_FILE, updated)
    return changed, updated


def environment_checks(project):
    editor = editor_path(project)
    checks = {
        "editor_path": editor,
        "editor_exists": bool(editor and Path(editor).exists()),
        "editor_path_source": "local-env" if local_env().get("unity_editor") else "project",
        "unity_version": project.get("engine", {}).get("version"),
        "library_exists": (ROOT / "Library").exists(),
        "package_cache_exists": (ROOT / "Library" / "PackageCache").exists(),
        "mcp_package_installed": any((ROOT / "Library" / "PackageCache").glob("com.coplaydev.unity-mcp*")),
        "python": sys.version.split()[0],
    }
    lock = unity_lock_state()
    checks["unity_project_locked"] = lock["locked"]
    checks["unity_editor_pid"] = lock.get("process_id")
    return checks


def pid_alive(pid):
    if not pid:
        return False
    try:
        proc = subprocess.run(
            ["tasklist", "/FI", "PID eq %d" % int(pid), "/NH", "/FO", "CSV"],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=20,
        )
    except (OSError, ValueError, subprocess.TimeoutExpired):
        return None
    return ('"%d"' % int(pid)) in (proc.stdout or "")


def unity_lock_state():
    info_path = ROOT / "Library" / "EditorInstance.json"
    lock_path = ROOT / "Temp" / "UnityLockfile"
    data = {}
    if info_path.exists():
        try:
            data = read_json(info_path)
        except HarnessError:
            data = {}
    pid = data.get("process_id") if isinstance(data, dict) else None
    lockfile_exists = lock_path.exists()
    alive = pid_alive(pid) if pid else False
    return {
        "editor_instance": data,
        "process_id": pid,
        "process_alive": bool(alive),
        "lockfile_exists": lockfile_exists,
        "stale_lock": bool(lockfile_exists and pid and alive is False),
        "locked": bool(alive) or (lockfile_exists and alive is None),
    }


def iter_compile_inputs():
    assets = ROOT / "Assets"
    if assets.exists():
        for path in sorted(assets.rglob("*")):
            if path.is_file() and path.suffix.lower() in COMPILE_INPUT_SUFFIXES:
                yield path, rel_path(path)
    for name in ("manifest.json", "packages-lock.json", "csc.rsp", "mcs.rsp"):
        path = ROOT / "Packages" / name
        if path.is_file():
            yield path, "Packages/" + name
    for name in ("ProjectVersion.txt", "ProjectSettings.asset"):
        path = ROOT / "ProjectSettings" / name
        if path.is_file():
            yield path, "ProjectSettings/" + name
    for name in ("csc.rsp", "mcs.rsp"):
        path = ROOT / name
        if path.is_file():
            yield path, name


def compile_signature():
    digest = hashlib.sha256()
    count = 0
    for path, rel in iter_compile_inputs():
        try:
            file_hash = sha256_file(path)
        except OSError as exc:
            file_hash = "unreadable:" + str(exc)
        digest.update(rel.encode("utf-8"))
        digest.update(b"\0")
        digest.update(file_hash.encode("utf-8"))
        digest.update(b"\n")
        count += 1
    return {"signature": "sha256:" + digest.hexdigest(), "file_count": count}


def parse_unity_log(text):
    lines = text.splitlines()
    compile_errors = []
    cs_warnings = []
    other_errors = []
    compile_failed = False
    for index, raw in enumerate(lines):
        line = raw.rstrip()
        if not line:
            continue
        if CS_ERROR_RE.search(line):
            compile_errors.append({"line": index + 1, "text": line.strip()[:500]})
        elif CS_WARNING_RE.search(line):
            cs_warnings.append({"line": index + 1, "text": line.strip()[:300]})
        elif "Scripts have compiler errors" in line or "Compilation failed" in line:
            compile_failed = True
            other_errors.append({"line": index + 1, "text": line.strip()[:500]})
        elif "error:" in line or line.strip().startswith("Shader error") or "Assembly has reference to non-existent" in line:
            other_errors.append({"line": index + 1, "text": line.strip()[:500]})
    excerpt = []
    seen = set()
    flagged = [item["line"] for item in compile_errors] + [item["line"] for item in other_errors[:20]]
    for number in flagged[:30]:
        for j in range(max(1, number - 2), min(len(lines), number + 2) + 1):
            if j in seen:
                continue
            seen.add(j)
            excerpt.append(lines[j - 1].rstrip()[:500])
        if len(excerpt) >= 80:
            break
    return {
        "compile_errors": compile_errors[:50],
        "warnings": cs_warnings[:20],
        "other_errors": other_errors[:20],
        "compile_failed": compile_failed or bool(compile_errors),
        "excerpt": excerpt[:80],
        "log_tail": [line.rstrip()[:500] for line in lines[-80:]],
        "line_count": len(lines),
    }


def unity_verify_summary(project=None):
    project = project or load_project()
    current = compile_signature()
    summary = {
        "present": False,
        "current_compile_signature": current["signature"],
        "compile_file_count": current["file_count"],
        "editor_evidence": None,
    }
    record = None
    if UNITY_VERIFY_FILE.exists():
        try:
            record = read_json(UNITY_VERIFY_FILE)
        except HarnessError as exc:
            summary["error"] = str(exc)
            return summary
    if record:
        drift = record.get("drift_after_compile") or []
        summary.update({
            "present": True,
            "result": record.get("result"),
            "mode": record.get("mode"),
            "verified_at": record.get("generated_at"),
            "compiled_at": record.get("compiled_at"),
            "compile_signature": record.get("compile_signature"),
            "error_count": record.get("error_count", 0),
            "warning_count": record.get("warning_count", 0),
            "compile_errors": record.get("compile_errors") or [],
            "log_path": record.get("log_path"),
            "stale": bool(record.get("compile_signature") != current["signature"] or drift),
            "drift_after_compile": drift,
        })
    if UNITY_VERIFY_EDITOR_FILE.exists():
        try:
            evidence = read_json(UNITY_VERIFY_EDITOR_FILE)
            unadopted = bool(evidence.get("compiled_at")) and not (
                record
                and record.get("mode") == "editor"
                and record.get("compiled_at") == evidence.get("compiled_at")
            )
            summary["editor_evidence"] = {
                "compiled_at": evidence.get("compiled_at"),
                "result": evidence.get("result"),
                "error_count": evidence.get("error_count"),
                "unadopted": unadopted,
            }
        except HarnessError:
            summary["editor_evidence"] = {"unadopted": False, "error": "证据文件无法解析"}
    return summary


def format_unity_verify(summary):
    if not summary.get("present"):
        evidence = summary.get("editor_evidence") or {}
        if evidence.get("unadopted"):
            return "未采纳编辑器证据（%s，%s；运行 unity-verify --adopt-editor）" % (
                evidence.get("result"), evidence.get("compiled_at"))
        return "无记录（运行 unity-verify 或 --adopt-editor）"
    text = "%s（%s，%s）" % (summary.get("result"), summary.get("mode"), summary.get("verified_at"))
    if summary.get("stale"):
        text += "；结果已过期"
    return text


def print_unity_verify_summary(summary):
    if not summary.get("present"):
        print("Unity 编译验证: 无记录")
        evidence = summary.get("editor_evidence") or {}
        if evidence.get("unadopted"):
            print("  发现未采纳的编辑器证据: %s（%s）" % (evidence.get("result"), evidence.get("compiled_at")))
            print("  运行: python .agent/scripts/agent.py unity-verify --adopt-editor")
        return
    print("Unity 编译验证: %s" % summary.get("result"))
    print("  模式: %s" % summary.get("mode"))
    print("  验证时间: %s" % summary.get("verified_at"))
    if summary.get("compiled_at"):
        print("  编译时间: %s" % summary.get("compiled_at"))
    print("  编译签名: %s（%d 个文件）%s" % (
        summary.get("compile_signature"), summary.get("compile_file_count") or 0,
        "，已过期" if summary.get("stale") else "，新鲜"))
    print("  errors=%s warnings=%s" % (summary.get("error_count"), summary.get("warning_count")))
    for item in (summary.get("compile_errors") or [])[:5]:
        text = item.get("text") or item.get("message") or ""
        print("  - " + str(text)[:240])
    if summary.get("drift_after_compile"):
        print("  编译后源文件有更新（可能未包含在证据中）: %s" % ", ".join(summary["drift_after_compile"][:5]))
    if summary.get("log_path"):
        print("  日志: %s" % summary["log_path"])


def adopt_editor_evidence(project, args):
    if not UNITY_VERIFY_EDITOR_FILE.exists():
        print("没有编辑器编译证据: %s" % UNITY_VERIFY_EDITOR_FILE.relative_to(ROOT).as_posix(), file=sys.stderr)
        return 2
    try:
        evidence = read_json(UNITY_VERIFY_EDITOR_FILE)
    except HarnessError as exc:
        print("编辑器证据无法解析: %s" % exc, file=sys.stderr)
        return 2
    if evidence.get("kind") != "unity-editor-compile-evidence":
        print("编辑器证据 kind 不正确: %s" % evidence.get("kind"), file=sys.stderr)
        return 2
    result = evidence.get("result")
    if result not in ("pass", "fail"):
        print("编辑器证据 result 无效: %s" % result, file=sys.stderr)
        return 2
    compiled_at = evidence.get("compiled_at")
    drift = []
    if compiled_at:
        try:
            compiled_ts = datetime.strptime(compiled_at, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc).timestamp()
            for path, rel in iter_compile_inputs():
                try:
                    if path.stat().st_mtime > compiled_ts + 1.0:
                        drift.append(rel)
                except OSError:
                    continue
        except ValueError:
            drift = []
    signature = compile_signature()
    git = git_info()
    errors = evidence.get("errors") or []
    warnings = evidence.get("warnings") or []
    record = {
        "schema_version": 1,
        "generated_at": now_iso(),
        "mode": "editor",
        "result": result,
        "compiled_at": compiled_at,
        "duration_seconds": evidence.get("duration_seconds"),
        "exit_code": None,
        "unity_version": evidence.get("unity_version") or project.get("engine", {}).get("version"),
        "editor_path": editor_path(project),
        "editor_pid": evidence.get("editor_pid"),
        "editor_instance": evidence.get("editor_instance"),
        "git_head": git["head"],
        "branch": git["branch"],
        "compile_signature": signature["signature"],
        "compile_file_count": signature["file_count"],
        "error_count": evidence.get("error_count", len(errors)),
        "warning_count": evidence.get("warning_count", len(warnings)),
        "compile_errors": errors[:50],
        "warnings": warnings[:20],
        "other_errors": [],
        "log_path": None,
        "drift_after_compile": drift[:20],
        "notes": [item for item in [evidence.get("note")] if item],
    }
    write_json(UNITY_VERIFY_FILE, record)
    append_event(
        "unity.verify",
        "采纳编辑器编译证据: %s" % result,
        {
            "result": result,
            "compiled_at": compiled_at,
            "compile_signature": signature["signature"],
            "error_count": record["error_count"],
            "warning_count": record["warning_count"],
            "drift": len(drift),
        },
    )
    if args.json:
        print(json.dumps(record, ensure_ascii=False, indent=2))
    else:
        print("已采纳编辑器编译证据。")
        print_unity_verify_summary(unity_verify_summary(project))
    return 0 if result == "pass" and not drift else 1


def cmd_unity_verify(args):
    project = load_project()
    if args.status:
        summary = unity_verify_summary(project)
        if args.json:
            print(json.dumps(summary, ensure_ascii=False, indent=2))
        else:
            print_unity_verify_summary(summary)
        if not summary.get("present"):
            return 2
        return 0 if summary.get("result") == "pass" and not summary.get("stale") else 1

    if args.adopt_editor:
        return adopt_editor_evidence(project, args)

    lock = unity_lock_state()
    if lock["locked"]:
        print("拒绝运行批处理编译：Unity 编辑器正在使用项目锁。", file=sys.stderr)
        if lock.get("process_id"):
            print("  编辑器 PID: %s" % lock["process_id"], file=sys.stderr)
        print("  关闭 Unity 后重试，或采纳编辑器内验证：", file=sys.stderr)
        print("  python .agent/scripts/agent.py unity-verify --adopt-editor", file=sys.stderr)
        append_event("unity.verify.refused", "批处理编译被拒绝：项目锁被占用",
                     {"process_id": lock.get("process_id")})
        return 3
    if lock["stale_lock"]:
        print("提示：发现残留项目锁（PID %s 已退出），继续运行。" % lock["process_id"])

    editor = editor_path(project)
    if not editor or not Path(editor).exists():
        print("找不到 Unity 编辑器: %s" % editor, file=sys.stderr)
        print('  在 .agent/.local/env.json 写入 {"unity_editor": "<path>"} 指定本机路径。', file=sys.stderr)
        return 2

    LOCAL.mkdir(parents=True, exist_ok=True)
    if UNITY_VERIFY_LOG.exists():
        try:
            UNITY_VERIFY_LOG.unlink()
        except OSError:
            pass
    command = [
        editor, "-batchmode", "-nographics", "-quit",
        "-projectPath", str(ROOT),
        "-logFile", str(UNITY_VERIFY_LOG),
    ]
    started = time.time()
    timed_out = False
    exit_code = None
    stdout = ""
    try:
        proc = subprocess.run(command, capture_output=True, text=True, encoding="utf-8",
                              errors="replace", timeout=args.timeout)
        exit_code = proc.returncode
        stdout = proc.stdout or ""
    except subprocess.TimeoutExpired:
        timed_out = True
    except OSError as exc:
        print("启动 Unity 失败: %s" % exc, file=sys.stderr)
        return 2
    duration = time.time() - started

    log_text = ""
    if UNITY_VERIFY_LOG.exists():
        try:
            log_text = UNITY_VERIFY_LOG.read_text(encoding="utf-8", errors="replace")
        except OSError:
            log_text = ""
    if not log_text:
        log_text = stdout
    parsed = parse_unity_log(log_text)
    license_error = any(
        "No valid Unity Editor license" in line or "Failed to activate/update license" in line
        for line in log_text.splitlines()
    )
    if timed_out:
        result = "timeout"
    elif parsed["compile_failed"]:
        result = "fail"
    elif exit_code not in (0, None) or license_error:
        result = "error"
    else:
        result = "pass"
    signature = compile_signature()
    git = git_info()
    record = {
        "schema_version": 1,
        "generated_at": now_iso(),
        "mode": "batchmode",
        "result": result,
        "compiled_at": now_iso(),
        "duration_seconds": round(duration, 1),
        "timed_out": timed_out,
        "exit_code": exit_code,
        "unity_version": project.get("engine", {}).get("version"),
        "editor_path": editor,
        "git_head": git["head"],
        "branch": git["branch"],
        "compile_signature": signature["signature"],
        "compile_file_count": signature["file_count"],
        "error_count": len(parsed["compile_errors"]),
        "warning_count": len(parsed["warnings"]),
        "compile_errors": parsed["compile_errors"],
        "warnings": parsed["warnings"],
        "other_errors": parsed["other_errors"],
        "log_path": UNITY_VERIFY_LOG.relative_to(ROOT).as_posix(),
        "log_line_count": parsed["line_count"],
        "log_tail": parsed["log_tail"],
        "excerpt": parsed["excerpt"],
        "license_error": license_error,
        "notes": [],
    }
    write_json(UNITY_VERIFY_FILE, record)
    append_event("unity.verify", "批处理编译验证: %s" % result, {
        "result": result,
        "duration_seconds": record["duration_seconds"],
        "exit_code": exit_code,
        "error_count": record["error_count"],
        "warning_count": record["warning_count"],
        "compile_signature": signature["signature"],
    })
    if args.json:
        print(json.dumps(record, ensure_ascii=False, indent=2))
    else:
        print("Unity 批处理编译验证: %s（%.1fs，exit=%s）" % (result, duration, exit_code))
        print("  编译签名: %s（%d 个文件）" % (signature["signature"], signature["file_count"]))
        print("  日志: %s（%d 行）" % (record["log_path"], record["log_line_count"]))
        if timed_out:
            print("  超时: %.0fs" % args.timeout)
        if record["compile_errors"]:
            print("  编译错误 (%d):" % record["error_count"])
            for item in record["compile_errors"][:5]:
                print("    " + item["text"][:240])
        if record["other_errors"] and result != "fail":
            print("  其他错误候选 (%d):" % len(record["other_errors"]))
            for item in record["other_errors"][:5]:
                print("    " + item["text"][:240])
        if record["excerpt"] and result != "pass":
            print("  日志片段:")
            for line in record["excerpt"][:30]:
                print("    " + line[:240])
    if result == "pass":
        return 0
    if result == "timeout":
        return 4
    if result == "error":
        return 2
    return 1


def unity_test_summary(project=None):
    project = project or load_project()
    current = compile_signature()
    summary = {
        "present": False,
        "current_compile_signature": current["signature"],
        "editor_evidence": None,
    }
    record = None
    if UNITY_TEST_FILE.exists():
        try:
            record = read_json(UNITY_TEST_FILE)
        except HarnessError as exc:
            summary["error"] = str(exc)
            return summary
    if record:
        drift = record.get("drift_after_test") or []
        summary.update({
            "present": True,
            "result": record.get("result"),
            "test_mode": record.get("test_mode"),
            "verified_at": record.get("generated_at"),
            "finished_at": record.get("finished_at"),
            "total": record.get("total", 0),
            "passed": record.get("passed", 0),
            "failed": record.get("failed", 0),
            "skipped": record.get("skipped", 0),
            "inconclusive": record.get("inconclusive", 0),
            "failures": record.get("failures") or [],
            "stale": bool(record.get("compile_signature") != current["signature"] or drift),
            "drift_after_test": drift,
        })
    if UNITY_TEST_EDITOR_FILE.exists():
        try:
            evidence = read_json(UNITY_TEST_EDITOR_FILE)
            unadopted = bool(evidence.get("finished_at")) and not (
                record
                and record.get("mode") == "editor"
                and record.get("finished_at") == evidence.get("finished_at")
            )
            summary["editor_evidence"] = {
                "finished_at": evidence.get("finished_at"),
                "result": evidence.get("result"),
                "total": evidence.get("total"),
                "failed": evidence.get("failed"),
                "unadopted": unadopted,
            }
        except HarnessError:
            summary["editor_evidence"] = {"unadopted": False, "error": "证据文件无法解析"}
    return summary


def format_unity_test(summary):
    if not summary.get("present"):
        evidence = summary.get("editor_evidence") or {}
        if evidence.get("unadopted"):
            return "未采纳编辑器测试证据（%s，%s；运行 unity-test --adopt-editor）" % (
                evidence.get("result"), evidence.get("finished_at"))
        return "无记录（编辑器菜单 Tools/Harness/Run EditMode Tests）"
    text = "%s（%s，%s；total=%s failed=%s）" % (
        summary.get("result"), summary.get("test_mode"), summary.get("verified_at"),
        summary.get("total"), summary.get("failed"))
    if summary.get("stale"):
        text += "；结果已过期"
    return text


def print_unity_test_summary(summary):
    if not summary.get("present"):
        print("EditMode 测试: 无记录")
        evidence = summary.get("editor_evidence") or {}
        if evidence.get("unadopted"):
            print("  发现未采纳的编辑器测试证据: %s（%s，total=%s failed=%s）" % (
                evidence.get("result"), evidence.get("finished_at"), evidence.get("total"), evidence.get("failed")))
            print("  运行: python .agent/scripts/agent.py unity-test --adopt-editor")
        else:
            print("  在 Unity 中运行菜单: Tools/Harness/Run EditMode Tests")
        return
    print("EditMode 测试: %s" % summary.get("result"))
    print("  模式: %s" % summary.get("test_mode"))
    print("  验证时间: %s" % summary.get("verified_at"))
    print("  完成时间: %s" % summary.get("finished_at"))
    print("  total=%s passed=%s failed=%s skipped=%s inconclusive=%s" % (
        summary.get("total"), summary.get("passed"), summary.get("failed"),
        summary.get("skipped"), summary.get("inconclusive")))
    print("  编译签名: %s %s" % (
        summary.get("current_compile_signature"),
        "，已过期" if summary.get("stale") else "，新鲜"))
    for item in (summary.get("failures") or [])[:5]:
        print("  - %s: %s" % (item.get("name"), str(item.get("message"))[:200]))


def adopt_test_evidence(project, args):
    if not UNITY_TEST_EDITOR_FILE.exists():
        print("没有编辑器测试证据: %s" % UNITY_TEST_EDITOR_FILE.relative_to(ROOT).as_posix(), file=sys.stderr)
        return 2
    try:
        evidence = read_json(UNITY_TEST_EDITOR_FILE)
    except HarnessError as exc:
        print("编辑器测试证据无法解析: %s" % exc, file=sys.stderr)
        return 2
    if evidence.get("kind") != "unity-test-editor-evidence":
        print("编辑器测试证据 kind 不正确: %s" % evidence.get("kind"), file=sys.stderr)
        return 2
    result = evidence.get("result")
    if result not in ("pass", "fail", "empty"):
        print("编辑器测试证据 result 无效: %s" % result, file=sys.stderr)
        return 2
    finished_at = evidence.get("finished_at")
    drift = []
    if finished_at:
        try:
            finished_ts = datetime.strptime(finished_at, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc).timestamp()
            for path, rel in iter_compile_inputs():
                try:
                    if path.stat().st_mtime > finished_ts + 1.0:
                        drift.append(rel)
                except OSError:
                    continue
        except ValueError:
            drift = []
    signature = compile_signature()
    git = git_info()
    record = {
        "schema_version": 1,
        "generated_at": now_iso(),
        "mode": "editor",
        "result": result,
        "test_mode": evidence.get("test_mode") or "EditMode",
        "started_at": evidence.get("started_at"),
        "finished_at": finished_at,
        "duration_seconds": evidence.get("duration_seconds"),
        "unity_version": evidence.get("unity_version") or project.get("engine", {}).get("version"),
        "editor_pid": evidence.get("editor_pid"),
        "editor_instance": evidence.get("editor_instance"),
        "git_head": git["head"],
        "branch": git["branch"],
        "compile_signature": signature["signature"],
        "compile_file_count": signature["file_count"],
        "total": evidence.get("total", 0),
        "passed": evidence.get("passed", 0),
        "failed": evidence.get("failed", 0),
        "skipped": evidence.get("skipped", 0),
        "inconclusive": evidence.get("inconclusive", 0),
        "failures": (evidence.get("failures") or [])[:20],
        "drift_after_test": drift[:20],
        "notes": [item for item in [evidence.get("note")] if item],
    }
    write_json(UNITY_TEST_FILE, record)
    append_event("unity.test", "采纳编辑器 EditMode 测试证据: %s" % result, {
        "result": result,
        "finished_at": finished_at,
        "total": record["total"],
        "failed": record["failed"],
        "compile_signature": signature["signature"],
        "drift": len(drift),
    })
    if args.json:
        print(json.dumps(record, ensure_ascii=False, indent=2))
    else:
        print("已采纳编辑器测试证据。")
        print_unity_test_summary(unity_test_summary(project))
    return 0 if result == "pass" and not drift else 1


def cmd_unity_test(args):
    project = load_project()
    if args.adopt_editor:
        return adopt_test_evidence(project, args)
    summary = unity_test_summary(project)
    if args.json:
        print(json.dumps(summary, ensure_ascii=False, indent=2))
    else:
        print_unity_test_summary(summary)
        print("  测试在编辑器内运行：菜单 Tools/Harness/Run EditMode Tests（不关闭 Unity）。")
    if not summary.get("present"):
        return 2
    return 0 if summary.get("result") == "pass" and not summary.get("stale") else 1


def cmd_session_start(args):
    project = load_project()
    git = git_info()
    stale, reason, manifest = index_status(project)
    rebuilt = False
    if stale and not args.no_index:
        print("索引 %s，正在重建..." % reason)
        manifest = rebuild_indexes(record_event=True)
        rebuilt = True
        stale, reason = False, "索引新鲜"
    state_changed, state = sync_state_from_git(project)
    validation = validate_all(project=project, write_local=True)
    active = load_active(create=True)
    env = environment_checks(project)
    watch = watcher_status()
    verify = unity_verify_summary(project)
    tests = unity_test_summary(project)
    events = read_events(limit=5)
    runtime = read_local_runtime()
    runtime["last_session_start"] = now_iso()
    write_json(LOCAL / "runtime.json", runtime)

    payload = {
        "git": git,
        "index": {"rebuilt": rebuilt, "stale": stale, "reason": reason, "manifest": manifest},
        "validation": validation,
        "state": state,
        "active": active,
        "environment": env,
        "watcher": watch,
        "unity_verify": verify,
        "unity_test": tests,
        "recent_events": events,
    }
    if args.json:
        print(json.dumps(payload, ensure_ascii=False, indent=2))
        return 1 if validation["errors"] else 0

    print("== 项目上下文 ==")
    print("项目: %s（%s %s）" % (project.get("name"), project.get("kind"), project.get("engine", {}).get("version")))
    print("分支: %s  HEAD: %s  %s" % (git["branch"], git["head"], "工作树有改动" if git["dirty"] else "工作树干净"))
    if git["ahead"] is not None:
        print("上游: ahead %s / behind %s" % (git["ahead"], git["behind"]))
    if git["status_lines"]:
        print("未提交改动:")
        for line in git["status_lines"][:50]:
            print("  " + line)
        if len(git["status_lines"]) > 50:
            print("  ... 其余 %d 条" % (len(git["status_lines"]) - 50))
    print("\n索引: %s%s" % ("已重建，" if rebuilt else "", "新鲜" if not stale else reason))
    if manifest:
        print("  " + ", ".join("%s=%s" % (k, v) for k, v in manifest.get("counts", {}).items()))
    print("\n校验: errors=%d warnings=%d（%s）" % (
        len(validation["errors"]), len(validation["warnings"]), "通过" if validation["ok"] else "未通过"))
    for item in validation["errors"][:5]:
        print("  ERROR " + item)
    for item in validation["warnings"][:5]:
        print("  WARN  " + item)
    print("\n编译验证: %s" % format_unity_verify(verify))
    print("测试验证: %s" % format_unity_test(tests))
    task = active.get("task")
    print("\n当前任务: %s" % (("%s (%s)" % (task.get("title"), task.get("status"))) if task else "无"))
    if task and task.get("next_action"):
        print("  任务下一步: %s" % task["next_action"])
    print("下一步: %s" % state.get("next_action"))
    if state.get("blockers"):
        print("阻塞: %s" % "; ".join(state["blockers"]))
    print("\n实时感知: %s" % watch["detail"])
    print("环境: Unity=%s（%s）Python=%s Library=%s MCP包=%s" % (
        env["unity_version"],
        "编辑器存在" if env["editor_exists"] else "编辑器路径不存在",
        env["python"],
        "存在" if env["library_exists"] else "不存在",
        "已安装（连接状态需由会话工具确认）" if env["mcp_package_installed"] else "未安装",
    ))
    if events:
        print("最近事件:")
        for event in events:
            print("  %s %-18s %s" % (event.get("ts"), event.get("type"), event.get("summary")))
    if state_changed:
        print("\n已把 Git 状态同步到 state.json。")
    return 1 if validation["errors"] else 0


def parse_check_arg(value):
    parts = [part.strip() for part in value.split("|")]
    name = parts[0] if parts else "check"
    status = parts[1] if len(parts) > 1 else "unverified"
    detail = " | ".join(parts[2:]) if len(parts) > 2 else ""
    if status not in ("pass", "fail", "skip", "manual", "unverified"):
        status = "unverified"
    return {"name": name, "command": "", "status": status, "detail": detail}


def cmd_checkpoint(args):
    project = load_project()
    stale, reason, _ = index_status(project)
    if stale and not args.no_index:
        print("索引 %s，重建后记录交接..." % reason)
        rebuild_indexes(record_event=True)
    git = git_info()
    state = load_state(project, create=True)
    blockers = list(state.get("blockers", []))
    for blocker in args.blocker or []:
        if blocker not in blockers:
            blockers.append(blocker)
    custom_checks = [parse_check_arg(item) for item in (args.check or [])]
    generated_at = now_iso()
    handoff = {
        "schema_version": 1,
        "generated_at": generated_at,
        "session": args.session or generated_at,
        "completed": args.completed or ([args.summary] if args.summary else []),
        "incomplete": args.incomplete or [],
        "files_changed": git["status_lines"][:200],
        "checks": custom_checks,
        "not_run": args.not_run or [],
        "next_action": args.next,
        "blockers": blockers,
    }
    write_json(HANDOFF_FILE, handoff)
    state["updated_at"] = generated_at
    state["branch"] = git["branch"]
    state["last_verified_commit"] = git["head"]
    state["working_tree"] = "dirty" if git["dirty"] else "clean"
    state["working_tree_summary"] = git["status_lines"][:50]
    state["blockers"] = blockers
    state["next_action"] = args.next
    if args.task_status:
        state["task_status"] = args.task_status
    write_json(STATE_FILE, state)
    validation = validate_all(project=project, write_local=True)
    auto_checks = [{
        "name": "harness.validate",
        "command": "python .agent/scripts/agent.py validate",
        "status": "pass" if validation["ok"] else "fail",
        "detail": "errors=%d warnings=%d" % (len(validation["errors"]), len(validation["warnings"])),
    }]
    auto_names = {check["name"] for check in auto_checks}
    checks = auto_checks + [check for check in custom_checks if check["name"] not in auto_names]
    handoff["checks"] = checks
    write_json(HANDOFF_FILE, handoff)
    stale, reason, manifest = index_status(project)
    state["index"] = {
        "generated_at": manifest.get("generated_at") if manifest else None,
        "signature": manifest.get("signature") if manifest else None,
        "stale": stale,
    }
    write_json(STATE_FILE, state)
    append_event("session.checkpoint", "会话交接：%s" % (args.summary or "已记录"),
                 {"next_action": args.next, "blockers": blockers, "checks": checks})
    print("交接已记录：tasks/handoff.json")
    print("下一步: %s" % args.next)
    print("校验: %s（errors=%d warnings=%d）" % ("通过" if validation["ok"] else "未通过", len(validation["errors"]), len(validation["warnings"])))
    return 0 if validation["ok"] else 1


def cmd_event(args):
    details = {}
    for item in args.detail or []:
        if "=" in item:
            key, value = item.split("=", 1)
            details[key] = value
        else:
            details[item] = True
    event = append_event(args.type, args.summary, details, actor=args.actor)
    print(json.dumps(event, ensure_ascii=False))
    return 0


def write_watch_state(**updates):
    path = LOCAL / "watch.json"
    info = {}
    if path.exists():
        try:
            info = read_json(path)
        except HarnessError:
            info = {}
    info.update(updates)
    info.setdefault("pid", os.getpid())
    write_json(path, info)
    return info


def cmd_mark_stale(args):
    info = write_watch_state(
        stale=True,
        reason=args.reason,
        changed_at=now_iso(),
        heartbeat=now_iso(),
    )
    print("索引已标记为过期：%s" % args.reason)
    return 0


def cmd_watch(args):
    project = load_project()
    current = light_signature(project)
    if args.once:
        previous = {}
        watch_path = LOCAL / "watch.json"
        if watch_path.exists():
            try:
                previous = read_json(watch_path)
            except HarnessError:
                previous = {}
        old_signature = previous.get("signature")
        changed = bool(old_signature and old_signature != current)
        write_watch_state(
            heartbeat=now_iso(), signature=current, interval=args.interval,
            stale=changed, reason="watch: single scan" if changed else "watch: single scan, no change",
            changed_at=now_iso() if changed else previous.get("changed_at"),
            pid=os.getpid(), detail="single scan",
        )
        if changed:
            print("检测到变化（单次扫描），索引已标记为过期。", flush=True)
            append_event("watch.change_detected", "监听单次扫描检测到源文件变化",
                         {"signature": current, "rebuild": bool(args.rebuild)})
            if args.rebuild:
                rebuild_indexes(record_event=True)
                write_watch_state(stale=False, reason="rebuilt", heartbeat=now_iso())
        else:
            print("单次扫描：未检测到变化。" if old_signature else "单次扫描：已记录基线签名。", flush=True)
        write_watch_state(heartbeat=None, detail="single scan finished")
        return 0
    last = current
    previous = current
    write_watch_state(
        heartbeat=now_iso(), signature=last, interval=args.interval,
        stale=False, pid=os.getpid(), detail="started",
    )
    print("监听中（interval=%ss, debounce=%ss%s），Ctrl+C 结束。" % (
        args.interval, args.debounce, ", 自动重建" if args.rebuild else ""), flush=True)
    try:
        while True:
            time.sleep(args.interval)
            current = light_signature(project)
            write_watch_state(heartbeat=now_iso(), interval=args.interval)
            if current == previous:
                continue
            time.sleep(args.debounce)
            current = light_signature(project)
            if current == last:
                continue
            last = current
            previous = current
            print("[%s] 检测到变化，标记索引过期。" % now_iso(), flush=True)
            write_watch_state(
                heartbeat=now_iso(), signature=current, stale=True,
                reason="watch: source changed", changed_at=now_iso(),
            )
            append_event("watch.change_detected", "监听检测到源文件变化",
                         {"signature": current, "rebuild": bool(args.rebuild)})
            if args.rebuild:
                rebuild_indexes(record_event=True)
                write_watch_state(stale=False, reason="rebuilt", heartbeat=now_iso())
    except KeyboardInterrupt:
        print("\n监听结束。")
    write_watch_state(heartbeat=None, stale=True, reason="watch stopped", pid=os.getpid())
    return 0


HOOK_TEMPLATE = """#!/bin/sh
{marker}
# Do not edit this block; managed by .agent/scripts/agent.py
cd "$(git rev-parse --show-toplevel)" || exit 0
python .agent/scripts/agent.py event --type git.{hook} --summary "{hook} $(git rev-parse --short HEAD 2>/dev/null)" >/dev/null 2>&1 || true
python .agent/scripts/agent.py mark-stale --reason "git.{hook}" >/dev/null 2>&1 || true
exit 0
"""


def hook_path(name):
    code, out, _ = run_git(["rev-parse", "--git-dir"])
    git_dir = (ROOT / out) if code == 0 else (ROOT / ".git")
    return git_dir / "hooks" / name


def cmd_hooks(args):
    names = ["post-commit", "post-checkout"]
    if args.action == "status":
        for name in names:
            path = hook_path(name)
            if not path.exists():
                print("%-14s 未安装" % name)
            else:
                text = path.read_text(encoding="utf-8", errors="replace")
                print("%-14s %s" % (name, "已安装（managed）" if HOOK_MARKER in text else "存在但非 managed，不会覆盖"))
        return 0
    if args.action == "install":
        for name in names:
            path = hook_path(name)
            if path.exists():
                text = path.read_text(encoding="utf-8", errors="replace")
                if HOOK_MARKER not in text:
                    print("拒绝覆盖已有自定义 hook：%s" % path, file=sys.stderr)
                    return 1
            path.parent.mkdir(parents=True, exist_ok=True)
            content = HOOK_TEMPLATE.format(marker=HOOK_MARKER, hook=name)
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(content)
            try:
                os.chmod(path, 0o755)
            except OSError:
                pass
            print("已安装 %s" % path)
        append_event("hooks.install", "安装 Git hooks 以标记索引过期", {"hooks": names})
        return 0
    if args.action == "uninstall":
        for name in names:
            path = hook_path(name)
            if not path.exists():
                print("%-14s 未安装" % name)
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            if HOOK_MARKER not in text:
                print("跳过非 managed hook：%s" % path, file=sys.stderr)
                continue
            path.unlink()
            print("已卸载 %s" % path)
        append_event("hooks.uninstall", "卸载 Git hooks", {"hooks": names})
        return 0
    raise HarnessError("unknown hooks action")


def build_parser():
    parser = argparse.ArgumentParser(description="Vindicator 项目 harness（会话启动、索引、校验、交接）")
    sub = parser.add_subparsers(dest="command", required=True)

    status = sub.add_parser("status", help="只读状态摘要")
    status.add_argument("--json", action="store_true")
    status.set_defaults(func=cmd_status)

    index = sub.add_parser("index", help="重建文件/资产/代码/依赖索引")
    index.add_argument("--quiet", action="store_true")
    index.set_defaults(func=cmd_index)

    start = sub.add_parser("session-start", help="会话启动：重建过期索引、校验、恢复任务上下文")
    start.add_argument("--json", action="store_true")
    start.add_argument("--no-index", action="store_true")
    start.set_defaults(func=cmd_session_start)

    validate = sub.add_parser("validate", help="校验 schema、路径、索引、资产完整性")
    validate.add_argument("--json", action="store_true")
    validate.add_argument("--strict", action="store_true", help="警告也视为失败")
    validate.set_defaults(func=cmd_validate)

    checkpoint = sub.add_parser("checkpoint", help="记录会话交接并更新 state")
    checkpoint.add_argument("--summary", default="")
    checkpoint.add_argument("--next", required=True)
    checkpoint.add_argument("--completed", action="append")
    checkpoint.add_argument("--incomplete", action="append", default=[])
    checkpoint.add_argument("--check", action="append", help="name|status|detail")
    checkpoint.add_argument("--not-run", action="append", default=[])
    checkpoint.add_argument("--blocker", action="append")
    checkpoint.add_argument("--session", default="")
    checkpoint.add_argument("--task-status", choices=["idle", "in_progress", "blocked", "done"])
    checkpoint.add_argument("--no-index", action="store_true")
    checkpoint.set_defaults(func=cmd_checkpoint)

    event = sub.add_parser("event", help="追加一条审计事件")
    event.add_argument("--type", required=True)
    event.add_argument("--summary", required=True)
    event.add_argument("--detail", action="append")
    event.add_argument("--actor", default="agent.py")
    event.set_defaults(func=cmd_event)

    stale = sub.add_parser("mark-stale", help="标记索引过期（供 hooks/watcher 使用）")
    stale.add_argument("--reason", default="external change")
    stale.set_defaults(func=cmd_mark_stale)

    watch = sub.add_parser("watch", help="前台轮询源文件变化（无常驻守护进程）")
    watch.add_argument("--interval", type=float, default=3.0)
    watch.add_argument("--debounce", type=float, default=1.5)
    watch.add_argument("--rebuild", action="store_true")
    watch.add_argument("--once", action="store_true")
    watch.set_defaults(func=cmd_watch)

    verify = sub.add_parser("unity-verify", help="Unity 编译验证（批处理；或采纳编辑器证据）")
    verify.add_argument("--status", action="store_true", help="只读最近一次编译验证记录")
    verify.add_argument("--adopt-editor", action="store_true", help="采纳编辑器写出的编译证据")
    verify.add_argument("--timeout", type=float, default=900.0, help="批处理超时秒数")
    verify.add_argument("--json", action="store_true")
    verify.set_defaults(func=cmd_unity_verify)

    tests = sub.add_parser("unity-test", help="EditMode 测试结果（编辑器内运行；只读/采纳证据）")
    tests.add_argument("--status", action="store_true", help="只读最近一次 EditMode 测试记录（默认行为）")
    tests.add_argument("--adopt-editor", action="store_true", help="采纳编辑器写出的测试证据")
    tests.add_argument("--json", action="store_true")
    tests.set_defaults(func=cmd_unity_test)

    hooks = sub.add_parser("hooks", help="安装/卸载 managed Git hooks（可选）")
    hooks.add_argument("action", choices=["status", "install", "uninstall"])
    hooks.set_defaults(func=cmd_hooks)
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except HarnessError as exc:
        print("harness error: %s" % exc, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
