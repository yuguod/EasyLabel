"""当 github.com 被代理拦掉、git push 走不通时，用 GitHub Git Data API 把本地提交原样搬上去。

思路：blob 内容直接取自 `git cat-file blob`（git 里存的字节），所以 blob/tree/commit 的 SHA
与本地完全一致，远程历史不会出现“内容一样但 SHA 不同”的分叉。
"""
import base64
import datetime
import json
import subprocess
import sys
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
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Authorization", "Bearer " + TOKEN)
    req.add_header("Accept", "application/vnd.github+json")
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            body = resp.read()
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as e:
        print(e.read().decode(errors="replace"))
        raise


def git(*args):
    return subprocess.run(["git"] + list(args), capture_output=True, check=True)


def raw_commit(sha):
    return git("cat-file", "commit", sha).stdout


def parse_commit(raw):
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


def main():
    head = git("rev-parse", "HEAD").stdout.decode().strip()
    raw = raw_commit(head)
    headers, message = parse_commit(raw)
    parent = headers[b"parent"].decode()
    tree = headers[b"tree"].decode()

    remote = api("GET", f"/repos/{REPO}/git/ref/heads/{BRANCH}")["object"]["sha"]
    print("local HEAD  =", head[:7])
    print("remote main =", remote[:7])
    if remote == head:
        print("已经一致，无需推送")
        return
    if remote != parent:
        sys.exit(f"远程 {remote[:7]} 不是本地 HEAD 的父提交 {parent[:7]}，先手动对齐再推")

    changed = git("diff-tree", "-r", "--no-commit-id", "--name-status", parent, head).stdout.decode().strip().splitlines()
    print(f"变更 {len(changed)} 个文件")

    entries = []
    for line in changed:
        parts = line.split("\t")
        status, path = parts[0], parts[-1]
        if status.startswith("D"):
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})
            print("  删除", path)
            continue

        blob_sha = git("rev-parse", f"{head}:{path}").stdout.decode().strip()
        content = git("cat-file", "blob", blob_sha).stdout
        mode = git("ls-tree", head, "--", path).stdout.decode().split(" ", 1)[0]

        created = api("POST", f"/repos/{REPO}/git/blobs", {
            "content": base64.b64encode(content).decode(),
            "encoding": "base64",
        })
        if created["sha"] != blob_sha:
            sys.exit(f"blob SHA 不一致：{path} 本地 {blob_sha} 远程 {created['sha']}")
        entries.append({"path": path, "mode": mode, "type": "blob", "sha": created["sha"]})
        print(f"  {'新增' if status.startswith('A') else '修改'} {path}  {blob_sha[:7]}")

    # 基线必须用“父提交的树”，HEAD 的树在远程还不存在。
    base_tree = api("GET", f"/repos/{REPO}/git/commits/{remote}")["tree"]["sha"]
    new_tree = api("POST", f"/repos/{REPO}/git/trees", {"base_tree": base_tree, "tree": entries})
    if new_tree["sha"] != tree:
        sys.exit(f"tree SHA 不一致：本地 {tree} 远程 {new_tree['sha']}")

    payload = {
        "message": message.decode("utf-8"),
        "tree": tree,
        "parents": [parent],
    }
    role_dates = {}
    for role in ("author", "committer"):
        name, _, rest = headers[role.encode()].decode().partition(" <")
        email, _, date = rest.partition("> ")
        payload[role] = {"name": name, "email": email, "date": iso_date(date)}
        role_dates[role] = email

    # 提交信息末尾的换行会改变 SHA：试几种写法，命中本地 SHA 的那个才是原文。
    candidates = [payload["message"], payload["message"].rstrip("\n"), payload["message"] + "\n"]
    commit = None
    for candidate in candidates:
        payload["message"] = candidate
        created = api("POST", f"/repos/{REPO}/git/commits", payload)
        if created["sha"] == head:
            commit = created
            break
    if commit is None:
        sys.exit(f"commit SHA 始终不一致：本地 {head}，最后得到 {created['sha']}")
    print("commit", commit["sha"][:7], "<-", payload["message"].splitlines()[0])

    api("PATCH", f"/repos/{REPO}/git/refs/heads/{BRANCH}", {"sha": head, "force": False})
    print("已更新", BRANCH, "->", head[:7])


if __name__ == "__main__":
    main()
