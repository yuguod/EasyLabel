"""当 github.com 被代理拦掉、git push 走不通时，用 GitHub Git Data API 把本地提交原样搬上去。

思路：blob 内容直接取自 `git cat-file blob`（git 里存的字节），所以 blob/tree/commit 的 SHA
与本地完全一致，远程历史不会出现“内容一样但 SHA 不同”的分叉。annotated tag 同理，
用 `git cat-file tag` 的原文重建，SHA 也对得上。

用法：
    python tools/push-api.py                 # 推送远程还没有的提交（默认 HEAD 之前的整段）
    python tools/push-api.py --tag v1.10.0   # 顺便把本地 annotated tag 推上去
    python tools/push-api.py --only <sha>    # 只推一个提交（要求它是远程 HEAD 的直接子提交）
"""
import base64
import datetime
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request

REPO = "yuguod/EasyLabel"
BRANCH = "main"
API = "https://api.github.com"


def token():
    return subprocess.run(["gh", "auth", "token"], capture_output=True, text=True, check=True).stdout.strip()


TOKEN = token()


def api(method, path, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    last = None
    for attempt in range(4):
        req = urllib.request.Request(API + path, data=data, method=method)
        req.add_header("Authorization", "Bearer " + TOKEN)
        req.add_header("Accept", "application/vnd.github+json")
        if data:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=90) as resp:
                body = resp.read()
                return json.loads(body) if body else {}
        except urllib.error.HTTPError as e:
            detail = e.read().decode(errors="replace")
            # 5xx 与限速可以重试，4xx（除 429）说明请求本身有问题，直接抛。
            if e.code < 500 and e.code != 429:
                print(detail)
                raise
            last = f"HTTP {e.code}: {detail}"
        except (urllib.error.URLError, TimeoutError, OSError) as e:
            last = f"{type(e).__name__}: {e}"
        print(f"  重试 {method} {path}（第 {attempt + 1} 次失败：{last}）")
        time.sleep(2 * (attempt + 1))
    raise SystemExit(f"API 多次失败：{method} {path} -> {last}")


def git(*args):
    return subprocess.run(["git"] + list(args), capture_output=True, check=True)


def raw_object(kind, sha):
    return git("cat-file", kind, sha).stdout


def parse_headers(raw):
    head, _, message = raw.partition(b"\n\n")
    headers = {}
    for line in head.split(b"\n"):
        if line.startswith(b" "):
            continue
        key, _, value = line.partition(b" ")
        headers[key] = value
    return headers, message


def iso_date(value):
    """git 里存的是 "1700000000 +0800"，API 要 ISO 8601，转一下带时区。"""
    stamp, _, tz = value.partition(" ")
    sign = -1 if tz.startswith("-") else 1
    offset = datetime.timedelta(hours=int(tz[1:3]), minutes=int(tz[3:5])) * sign
    return datetime.datetime.fromtimestamp(int(stamp), datetime.timezone(offset)).isoformat()


def split_signature(value):
    """"名字 <邮箱> 1700000000 +0800" -> (名字, 邮箱, ISO 时间)"""
    name, _, rest = value.decode().partition(" <")
    email, _, date = rest.partition("> ")
    return name, email, iso_date(date)


def remote_branch():
    try:
        return api("GET", f"/repos/{REPO}/git/ref/heads/{BRANCH}")["object"]["sha"]
    except urllib.error.HTTPError:
        return None


def push_commit(sha, parent_remote):
    """把本地提交 sha 原样搬到远程（父提交必须已在远程），返回新远程 HEAD。"""
    raw = raw_object("commit", sha)
    headers, message = parse_headers(raw)
    parent = headers[b"parent"].decode()
    tree = headers[b"tree"].decode()
    if parent_remote and parent != parent_remote:
        sys.exit(f"提交 {sha[:7]} 的父提交 {parent[:7]} 与远程 {parent_remote[:7]} 不一致，先手动对齐")
    print(f"提交 {sha[:7]}  父 {parent[:7]}")

    changed = git("diff-tree", "-r", "--no-commit-id", "--name-status", parent, sha).stdout.decode().strip().splitlines()
    entries = []
    for line in changed:
        parts = line.split("\t")
        status, path = parts[0], parts[-1]
        if status.startswith("D"):
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})
            print("  删除", path)
            continue

        blob_sha = git("rev-parse", f"{sha}:{path}").stdout.decode().strip()
        content = raw_object("blob", blob_sha)
        mode = git("ls-tree", sha, "--", path).stdout.decode().split(" ", 1)[0]

        created = api("POST", f"/repos/{REPO}/git/blobs", {
            "content": base64.b64encode(content).decode(),
            "encoding": "base64",
        })
        if created["sha"] != blob_sha:
            sys.exit(f"blob SHA 不一致：{path} 本地 {blob_sha} 远程 {created['sha']}")
        entries.append({"path": path, "mode": mode, "type": "blob", "sha": created["sha"]})
        print(f"  {'新增' if status.startswith('A') else '修改'} {path}  {blob_sha[:7]}")

    # 基线必须用“父提交的树”，本提交的树在远程还不存在。
    base_tree = api("GET", f"/repos/{REPO}/git/commits/{parent_remote}")["tree"]["sha"]
    new_tree = api("POST", f"/repos/{REPO}/git/trees", {"base_tree": base_tree, "tree": entries})
    if new_tree["sha"] != tree:
        sys.exit(f"tree SHA 不一致：本地 {tree} 远程 {new_tree['sha']}")

    payload = {"tree": tree, "parents": [parent]}
    for role in ("author", "committer"):
        name, email, date = split_signature(headers[role.encode()])
        payload[role] = {"name": name, "email": email, "date": date}

    # 提交信息末尾的换行会改变 SHA：试几种写法，命中本地 SHA 的那个才是原文。
    text = message.decode("utf-8")
    created = None
    for candidate in (text, text.rstrip("\n"), text + "\n"):
        payload["message"] = candidate
        created = api("POST", f"/repos/{REPO}/git/commits", payload)
        if created["sha"] == sha:
            break
    if created["sha"] != sha:
        sys.exit(f"commit SHA 始终不一致：本地 {sha}，最后得到 {created['sha']}")
    print(f"  -> {sha[:7]} {text.splitlines()[0] if text.strip() else '(无提交信息)'}")
    return sha


def push_tag(name):
    """重建 annotated tag 对象并创建 refs/tags/<name>，SHA 与本地一致。"""
    local = git("rev-parse", f"refs/tags/{name}").stdout.decode().strip()
    kind = git("cat-file", "-t", local).stdout.decode().strip()
    if kind != "tag":
        sys.exit(f"本地 {name} 是 {kind}，不是 annotated tag，无法保证 SHA 一致")

    try:
        existing = api("GET", f"/repos/{REPO}/git/ref/tags/{name}")["object"]["sha"]
        if existing == local:
            print(f"标签 {name} 已在远端且一致")
            return
        sys.exit(f"远端已有标签 {name} 指向 {existing[:7]}，与本地 {local[:7]} 不一致，先删掉再推")
    except urllib.error.HTTPError:
        pass

    raw = raw_object("tag", local)
    headers, message = parse_headers(raw)
    tagger = headers.get(b"tagger")
    payload = {
        "tag": headers[b"tag"].decode(),
        "message": message.decode("utf-8"),
        "object": headers[b"object"].decode(),
        "type": headers[b"type"].decode(),
    }
    if tagger:
        name_part, email, date = split_signature(tagger)
        payload["tagger"] = {"name": name_part, "email": email, "date": date}

    text = payload["message"]
    created = None
    for candidate in (text, text.rstrip("\n"), text + "\n"):
        payload["message"] = candidate
        created = api("POST", f"/repos/{REPO}/git/tags", payload)
        if created["sha"] == local:
            break
    if created["sha"] != local:
        sys.exit(f"tag SHA 不一致：本地 {local}，远程 {created['sha']}")

    api("POST", f"/repos/{REPO}/git/refs", {"ref": f"refs/tags/{name}", "sha": local})
    print(f"标签 {name} -> {created['object']['sha'][:7]}")


def main():
    args = sys.argv[1:]
    tag = None
    only = None
    if "--tag" in args:
        tag = args[args.index("--tag") + 1]
    if "--only" in args:
        only = args[args.index("--only") + 1]

    head = git("rev-parse", "HEAD").stdout.decode().strip()
    remote = remote_branch()
    print("local HEAD  =", head[:7])
    print("remote main =", remote[:7] if remote else "(无)")

    pending = []
    if remote != head:
        if not remote:
            sys.exit("远程分支不存在，请先手动初始化仓库")
        if only:
            pending = [only]
        else:
            pending = git("rev-list", "--reverse", f"{remote}..{head}").stdout.decode().split()
        if not pending:
            sys.exit(f"本地 {head[:7]} 不在远程 {remote[:7]} 之后，无法快进")

    for sha in pending:
        remote = push_commit(sha, remote)

    if pending and remote != head:
        sys.exit(f"推完之后远程 HEAD {remote[:7]} 与本地 {head[:7]} 不一致")

    if pending:
        api("PATCH", f"/repos/{REPO}/git/refs/heads/{BRANCH}", {"sha": remote, "force": False})
        print(f"已更新 {BRANCH} -> {remote[:7]}")
    elif remote == head:
        print("分支已一致，无需推送")

    if tag:
        push_tag(tag)


if __name__ == "__main__":
    main()
