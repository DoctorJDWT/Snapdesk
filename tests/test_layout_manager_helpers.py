"""Pure helper tests for layout_manager (no Win32 calls)."""

from __future__ import annotations

import os

import pytest

from layout_manager import (
    _browser_launch_argv,
    _looks_like_browser_url,
    _normalize_exe_path,
    _parse_window_pair_arg,
    _profile_path,
    _title_match,
)


@pytest.fixture
def local_app_data(tmp_path, monkeypatch):
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    return tmp_path


def test_profile_path_sanitizes_bad_chars(local_app_data):
    path = _profile_path("My/Bad:Name*")
    assert path.name == "My_Bad_Name.json"
    assert path.parent == local_app_data / "LayoutProfiles" / "profiles"


def test_normalize_exe_path_lowercases():
    assert _normalize_exe_path(r"C:\Program Files\Chrome.exe") == r"c:\program files\chrome.exe"
    assert _normalize_exe_path("") == ""


def test_title_match_prefix():
    assert _title_match("Notepad", "Notepad - untitled")
    assert _title_match("  Foo  Bar  ", "foo bar extra")


def test_title_match_substring():
    assert _title_match("League", "League of Legends (TM) Client")
    assert not _title_match("League", "Client")


def test_parse_window_pair_arg_valid():
    exe, title = _parse_window_pair_arg(r"C:\Apps\foo.exe|My Window")
    assert exe == os.path.normpath(r"c:\apps\foo.exe")
    assert title == "My Window"


def test_parse_window_pair_arg_invalid():
    with pytest.raises(ValueError, match=r"Expected exe_path\|title"):
        _parse_window_pair_arg("no-pipe-here")


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("https://example.com", True),
        ("http://localhost:8080/path", True),
        ("not-a-url", False),
        ("", False),
        (None, False),
    ],
)
def test_looks_like_browser_url(value, expected):
    assert _looks_like_browser_url(value) is expected


def test_browser_launch_argv_chrome_with_url():
    exe = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
    url = "https://example.com"
    argv = _browser_launch_argv(exe, url)
    assert argv == [exe, "--new-window", url]
