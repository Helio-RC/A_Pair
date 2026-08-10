#!/usr/bin/env python3
"""
release.py 单元测试。

在 /tmp 隔离环境中运行，mock 外部依赖（subprocess、oss2、requests）。
"""

import hashlib
import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch, MagicMock

SCRIPTS_DIR = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(SCRIPTS_DIR / "release"))

from release import (
    sha256_file,
    format_size,
    resolve_root,
    ReleaseManager,
)


def make_version_json(path: Path, version: str = "1.2.0"):
    """创建测试用 version.json。"""
    data = {
        "version": version,
        "commitId": "test1234",
        "buildDate": "2026-01-01T00:00:00+08:00",
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def make_release_md(path: Path):
    """创建测试用 RELEASE.md。"""
    path.write_text("# SeatFlow v1.2.0 Test\n\nTest release notes.", encoding="utf-8")


def make_config_json(path: Path):
    """创建测试用 config.json。"""
    data = {
        "oss": {
            "accessKeyId": "test-key",
            "accessKeySecret": "test-secret",
            "endpoint": "oss-cn-test.aliyuncs.com",
            "bucket": "test-bucket",
        },
        "github": {
            "repo": "test/repo",
            "token": "test-token",
        },
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


class TestSha256File(unittest.TestCase):
    """SHA256 计算测试。"""

    def test_known_content(self):
        fd, tmp_path = tempfile.mkstemp()
        os.close(fd)
        path = Path(tmp_path)
        path.write_bytes(b"hello world")
        expected = hashlib.sha256(b"hello world").hexdigest()
        self.assertEqual(sha256_file(path), expected)
        path.unlink()

    def test_empty_file(self):
        fd, tmp_path = tempfile.mkstemp()
        os.close(fd)
        path = Path(tmp_path)
        path.write_bytes(b"")
        expected = hashlib.sha256(b"").hexdigest()
        self.assertEqual(sha256_file(path), expected)
        path.unlink()


class TestFormatSize(unittest.TestCase):
    """文件大小格式化测试。"""

    def test_bytes(self):
        self.assertEqual(format_size(500), "500.0 B")

    def test_kib(self):
        self.assertEqual(format_size(2048), "2.0 KiB")

    def test_mib(self):
        self.assertEqual(format_size(10 * 1024 * 1024), "10.0 MiB")


class TestReleaseManager(unittest.TestCase):
    """ReleaseManager 集成测试（mock 外部依赖）。"""

    def setUp(self):
        self.tmpdir = Path(tempfile.mkdtemp(prefix="release-test-"))
        self.root = self.tmpdir

        # 创建文件结构
        make_version_json(self.root / "version.json", "1.2.0")
        make_release_md(self.root / "RELEASE.md")

        config_dir = self.root / "scripts" / "release"
        config_dir.mkdir(parents=True, exist_ok=True)
        make_config_json(config_dir / "config.json")

        # 创建 dist/ 目录结构（模拟已有构建）
        dist_dir = self.root / "dist" / "release" / "1.2.0"
        dist_dir.mkdir(parents=True, exist_ok=True)

        config_path = config_dir / "config.json"
        self.mgr = ReleaseManager(self.root, config_path, dry_run=True)

    def tearDown(self):
        if self.tmpdir.exists():
            shutil.rmtree(self.tmpdir)

    # ── 配置加载 ──

    def test_load_config_valid(self):
        cfg = self.mgr.config
        self.assertEqual(cfg["oss"]["bucket"], "test-bucket")
        self.assertEqual(cfg["github"]["repo"], "test/repo")

    def test_load_config_missing_file(self):
        with self.assertRaises(ValueError):
            ReleaseManager(self.root, self.root / "nonexistent.json")

    def test_load_config_missing_oss_key(self):
        cfg_path = self.root / "bad_config.json"
        data = {"oss": {"accessKeyId": "x"}, "github": {"repo": "x", "token": "x"}}
        cfg_path.write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaises(ValueError):
            ReleaseManager(self.root, cfg_path)

    # ── 版本信息 ──

    def test_load_version_info(self):
        v = self.mgr.version_info
        self.assertEqual(v["version"], "1.2.0")

    def test_version_info_missing_field(self):
        bad_path = self.root / "bad_version.json"
        bad_path.write_text('{"commitId": "abc", "buildDate": "2026-01-01"}', encoding="utf-8")
        mgr = ReleaseManager.__new__(ReleaseManager)
        mgr.version_json_path = bad_path
        with self.assertRaises(ValueError):
            mgr._load_version_info()

    # ── 发布说明 ──

    def test_build_release_notes(self):
        notes = self.mgr.build_release_notes()
        self.assertIn("SeatFlow v1.2.0 Test", notes)
        self.assertIn("Test release notes", notes)

    def test_release_notes_missing_file(self):
        self.mgr.release_md_path.unlink()
        with self.assertRaises(FileNotFoundError):
            self.mgr.build_release_notes()

    # ── 打包 ──

    def _make_vpk_manager(self):
        """创建非 dry-run 的 ReleaseManager（用于测试 vpk_pack_all 产物收集）。"""
        return ReleaseManager(
            self.root, self.root / "scripts" / "release" / "config.json",
            dry_run=False, skip_velopack=False, skip_branch_check=True)

    def _make_vpk_output(self, rid: str, files: list[str]) -> Path:
        """预置 Velopack 输出目录与伪造产物文件。"""
        out_dir = self.mgr.version_dist_dir / f"velopack_{rid}"
        out_dir.mkdir(parents=True, exist_ok=True)
        for name in files:
            (out_dir / name).write_bytes(b"fake installer")
        return out_dir

    @patch("release.run_command")
    def test_vpk_pack_all_windows_collects_artifacts(self, mock_cmd):
        """vpk [win] pack 成功后应收集安装程序与频道元数据产物。"""
        mock_cmd.return_value.returncode = 0
        mock_cmd.return_value.stdout = ""
        self._make_vpk_output("win-x64", [
            "SeatFlow-1.2.0-windows.exe",
            "releases.win-x64.json",
            "assets.win-x64.json",   # vpk 内部元数据，应被过滤
        ])

        mgr = self._make_vpk_manager()
        files = mgr.vpk_pack_all({"win-x64": self.root / "dummy-publish"})

        self.assertEqual(len(files), 2)
        f = files[0]
        self.assertEqual(f["platform"], "windows")
        self.assertEqual(f["rid"], "win-x64")
        self.assertEqual(len(f["sha256"]), 64)
        self.assertTrue(Path(f["localPath"]).exists())
        self.assertEqual(Path(f["localPath"]).name, "SeatFlow-1.2.0-windows.exe")

    @patch("release.run_command")
    def test_vpk_pack_all_linux_collects_artifacts(self, mock_cmd):
        """vpk [linux] pack 成功后应收集 AppImage 产物。"""
        mock_cmd.return_value.returncode = 0
        mock_cmd.return_value.stdout = ""
        self._make_vpk_output("linux-x64", [
            "SeatFlow-1.2.0-x64.AppImage",
            "releases.linux-x64.json",
        ])

        mgr = self._make_vpk_manager()
        files = mgr.vpk_pack_all({"linux-x64": self.root / "dummy-publish"})

        self.assertEqual(len(files), 2)
        self.assertTrue(all(f["platform"] == "linux" for f in files))
        self.assertTrue(any("AppImage" in f["fileName"] for f in files))
        self.assertTrue(all(len(f["sha256"]) == 64 for f in files))

    @patch("release.run_command")
    def test_vpk_pack_all_skips_missing_rid(self, mock_cmd):
        """无构建产物的 RID 应跳过，不执行 vpk pack。"""
        mgr = self._make_vpk_manager()
        files = mgr.vpk_pack_all({})  # 无任何 RID 产物
        self.assertEqual(files, [])
        mock_cmd.assert_not_called()

    def test_vpk_pack_all_dry_run_short_circuits(self):
        """dry-run 模式应跳过 Velopack 打包并返回空列表。"""
        files = self.mgr.vpk_pack_all({"win-x64": self.root / "dummy-publish"})
        self.assertEqual(files, [])

    # ── Release Body ──

    def test_build_release_body(self):
        files = [
            {"fileName": "SeatFlow-1.2.0-windows.exe", "sha256": "abc123", "size": 100,
             "platform": "windows", "localPath": "/tmp/SeatFlow-1.2.0-windows.exe"},
        ]
        body = self.mgr._build_release_body("# Notes", files)
        self.assertIn("# Notes", body)
        self.assertIn("### SHA256 Checksums", body)
        self.assertIn("| SeatFlow-1.2.0-windows.exe | abc123 |", body)

    def test_build_release_body_no_installers(self):
        """无安装程序产物时（如仅更新包），不附加 SHA256 表格。"""
        files = [
            {"fileName": "updates/SeatFlow-1.2.0-full.nupkg", "sha256": "def456", "size": 100,
             "platform": "windows", "localPath": "/tmp/x.nupkg"},
        ]
        body = self.mgr._build_release_body("# Notes", files)
        self.assertIn("# Notes", body)
        self.assertNotIn("### SHA256 Checksums", body)

    # ── Dry Run ──

    def test_dry_run_skips_oss(self):
        self.assertTrue(self.mgr.dry_run)

    # ── releases.json 重复版本检测 ──

    @patch("release.ReleaseManager._oss_put")
    def test_releases_index_duplicate_version(self, mock_put):
        """模拟 OSS 已有同版本 → 应抛出 ValueError。"""
        existing = {
            "latest": "1.2.0",
            "versions": [{"version": "1.2.0", "files": []}],
        }

        mock_bucket = MagicMock()
        mock_bucket.get_object.return_value.read.return_value = json.dumps(existing).encode()

        files = [{"platform": "windows", "fileName": "test.zip",
                   "size": 100, "sha256": "abc", "localPath": "/tmp/test.zip"}]

        with self.assertRaises(ValueError):
            self.mgr._update_releases_index(mock_bucket, files, "# Notes")

    def test_releases_index_new_version(self):
        """OSS 无同版本 → 应成功插入。"""
        existing = {
            "latest": "1.0.0",
            "versions": [{"version": "1.0.0", "notes": "old", "files": []}],
        }

        mock_bucket = MagicMock()
        mock_bucket.get_object.return_value.read.return_value = json.dumps(existing).encode()

        files = [{"platform": "windows", "fileName": "SeatFlow-1.2.0-windows.exe",
                   "size": 100, "sha256": "abc", "localPath": "/tmp/test.exe"}]

        self.mgr._update_releases_index(mock_bucket, files, "# Notes")
        mock_bucket.put_object.assert_called_once()
        # 上传内容应为更新后的索引（含新版本 1.2.0）
        uploaded = mock_bucket.put_object.call_args.args[1]
        parsed = json.loads(uploaded)
        self.assertEqual(parsed["latest"], "1.2.0")
        self.assertEqual(parsed["versions"][0]["version"], "1.2.0")


if __name__ == "__main__":
    unittest.main(verbosity=2)
