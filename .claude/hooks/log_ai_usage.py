#!/usr/bin/env python3
"""Stop hook: append the latest user question + assistant answer to AI_LOG.md.

Reads the Stop-hook JSON payload on stdin (transcript_path, cwd, session_id),
finds the last genuine user text message in the session transcript and the
assistant text produced in reply to it, and appends a timestamped entry to
AI_LOG.md in the project root. Idempotent across repeated Stop events (e.g.
/clear, /resume, /compact) via a small per-session state file.
"""
import json
import os
import re
import sys
from datetime import datetime

STATE_DIR = os.path.join(os.path.dirname(__file__), ".state")


def read_hook_input():
    try:
        return json.load(sys.stdin)
    except Exception:
        return {}


def extract_text_blocks(content):
    """Return concatenated 'text' blocks from a message content list/string."""
    if isinstance(content, str):
        return content.strip()
    if isinstance(content, list):
        parts = [b.get("text", "") for b in content if isinstance(b, dict) and b.get("type") == "text"]
        return "\n".join(p for p in parts if p).strip()
    return ""


def has_only_tool_result(content):
    if not isinstance(content, list):
        return False
    types = [b.get("type") for b in content if isinstance(b, dict)]
    return bool(types) and all(t == "tool_result" for t in types)


def is_human_typed(entry):
    """True only for messages the user actually typed (not skill/tool-injected
    text, which shares the same type=='user' shape but has no human origin)."""
    return isinstance(entry.get("origin"), dict) and entry["origin"].get("kind") == "human"


def load_transcript(path):
    entries = []
    try:
        with open(path, "r") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    entries.append(json.loads(line))
                except json.JSONDecodeError:
                    continue
    except FileNotFoundError:
        pass
    return entries


def find_last_qa(entries):
    """Return (question_text, question_uuid, answer_text) or (None, None, None)."""
    last_user_idx = None
    for i in range(len(entries) - 1, -1, -1):
        e = entries[i]
        if e.get("type") != "user":
            continue
        if not is_human_typed(e):
            continue
        msg = e.get("message", {})
        if msg.get("role") != "user":
            continue
        content = msg.get("content")
        if has_only_tool_result(content):
            continue
        text = extract_text_blocks(content)
        if not text:
            continue
        last_user_idx = i
        question_text = text
        question_uuid = e.get("uuid")
        break

    if last_user_idx is None:
        return None, None, None

    answer_parts = []
    for e in entries[last_user_idx + 1:]:
        if e.get("type") != "assistant":
            continue
        msg = e.get("message", {})
        if msg.get("role") != "assistant":
            continue
        text = extract_text_blocks(msg.get("content"))
        if text:
            answer_parts.append(text)

    answer_text = "\n\n".join(answer_parts).strip()
    return question_text, question_uuid, answer_text


def squash(text, limit):
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) > limit:
        text = text[:limit].rstrip() + "..."
    return text


def load_state(session_id):
    os.makedirs(STATE_DIR, exist_ok=True)
    path = os.path.join(STATE_DIR, f"{session_id}.json")
    try:
        with open(path) as f:
            return json.load(f), path
    except Exception:
        return {}, path


def save_state(path, state):
    try:
        with open(path, "w") as f:
            json.dump(state, f)
    except Exception:
        pass


def main():
    payload = read_hook_input()
    transcript_path = payload.get("transcript_path")
    cwd = payload.get("cwd") or os.getcwd()
    session_id = payload.get("session_id", "unknown")

    if not transcript_path:
        return

    entries = load_transcript(transcript_path)
    question, question_uuid, answer = find_last_qa(entries)

    if not question or not answer:
        return

    state, state_path = load_state(session_id)
    if state.get("last_logged_uuid") == question_uuid:
        return  # nothing new since last log

    log_path = os.path.join(cwd, "AI_LOG.md")
    is_new = not os.path.exists(log_path)

    timestamp = datetime.now().astimezone().strftime("%Y-%m-%d %H:%M:%S %Z")
    q_line = squash(question, 300)
    a_line = squash(answer, 500)

    entry = f"## {timestamp}\n\n**Q:** {q_line}\n\n**A:** {a_line}\n\n"

    with open(log_path, "a") as f:
        if is_new:
            f.write("# AI Usage Log\n\n")
            f.write("Auto-generated log of questions asked and answers given in Claude Code sessions.\n\n")
        f.write(entry)

    state["last_logged_uuid"] = question_uuid
    save_state(state_path, state)


if __name__ == "__main__":
    main()
