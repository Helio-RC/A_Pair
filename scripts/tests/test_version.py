#!/usr/bin/env python3
"""
version.py 单元测试。

在 /tmp 隔离环境中运行，不触碰真实项目文件。
"""

import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(SCRIPTS_DIR))

from version import (
    bump_version,
    resolve_root,
    VersionManager,
    APP_VERSION_RE,
    FILE_VERSION_RE,
    STRATEGY_VERSION_RE,
    FILE_TYPE_TO_MODEL,
    MODEL_VERSION_RE,
)


def make_version_json(path: Path, version: str):
    """创建 version.json。"""
    data = {
        "version": version,
        "commitId": "test1234",
        "buildDate": "2026-01-01T00:00:00+08:00",
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def make_about_json(path: Path, version: str):
    """创建 about.json。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    data = {
        "zh-CN": {"version": version, "description": "测试"},
        "en-US": {"version": version, "description": "Test"},
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def make_file_versions_json(path: Path, versions: dict[str, str]):
    """创建 file_versions.json。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(versions, f, ensure_ascii=False, indent=2)


def make_model_cs(path: Path, version: str):
    """创建 Model C# 文件。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    content = f'''using System;

namespace SeatFlow.Core.Models;

public class TestModel
{{
    public string Version {{ get; set; }} = "{version}";
}}
'''
    path.write_text(content, encoding="utf-8")


def make_manifest_json(path: Path, version: str, manifest_version: str = "1.0"):
    """创建策略清单 JSON。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    data = {
        "id": path.stem,
        "version": version,
        "manifestVersion": manifest_version,
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def make_onboarding_json(path: Path, version: str):
    """创建 onboarding_config.json。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    data = {"version": version, "startupPhases": []}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


class TestBumpVersion(unittest.TestCase):
    """语义化版本号递增测试。"""

    def test_major(self):
        self.assertEqual(bump_version("1.2.0", "major"), "2.0.0")

    def test_minor(self):
        self.assertEqual(bump_version("1.2.0", "minor"), "1.3.0")

    def test_patch(self):
        self.assertEqual(bump_version("1.2.0", "patch"), "1.2.1")

    def test_rollover(self):
        self.assertEqual(bump_version("1.9.0", "minor"), "1.10.0")


class TestVersionRegex(unittest.TestCase):
    """版本格式校验测试。"""

    def test_app_version_valid(self):
        self.assertTrue(APP_VERSION_RE.match("1.2.0"))

    def test_app_version_invalid(self):
        self.assertIsNone(APP_VERSION_RE.match("1.2"))
        self.assertIsNone(APP_VERSION_RE.match("v1.2.0"))
        self.assertIsNone(APP_VERSION_RE.match(""))

    def test_file_version_valid(self):
        self.assertTrue(FILE_VERSION_RE.match("1.1"))

    def test_file_version_invalid(self):
        self.assertIsNone(FILE_VERSION_RE.match("1.1.0"))
        self.assertIsNone(FILE_VERSION_RE.match("1"))

    def test_strategy_version_valid(self):
        self.assertTrue(STRATEGY_VERSION_RE.match("1.0.0"))


class TestVersionManager(unittest.TestCase):
    """VersionManager 集成测试。"""

    def setUp(self):
        self.tmpdir = Path(tempfile.mkdtemp(prefix="version-test-"))
        self.root = self.tmpdir

        # 创建完整目录结构
        about_dir = self.root / "SeatFlow.Presentation.Avalonia/Data"
        fv_dir = self.root / "SeatFlow.Infrastructure/Migration"
        model_dir = self.root / "SeatFlow.Core/Models"
        manifest_dir = self.root / "SeatFlow.Core/Strategies/Manifests"
        provider_dir = self.root / "SeatFlow.Infrastructure/Providers"
        service_dir = self.root / "SeatFlow.Core/Services"

        for d in [about_dir, fv_dir, model_dir, manifest_dir, provider_dir, service_dir]:
            d.mkdir(parents=True, exist_ok=True)

        # version.json
        make_version_json(self.root / "version.json", "1.2.0")

        # about.json
        make_about_json(about_dir / "about.json", "1.2.0")

        # file_versions.json
        make_file_versions_json(fv_dir / "file_versions.json", {
            "venue": "1.1", "roster": "1.1", "snapshot": "1.0",
            "venueInfo": "1.0", "appSettings": "1.0",
            "strategyConfig": "1.0", "strategyDatasetConfig": "1.0",
        })

        # Model 类
        for ftype, fname in FILE_TYPE_TO_MODEL.items():
            ver = "1.1" if ftype in ("venue", "roster") else "1.0"
            make_model_cs(model_dir / fname, ver)

        # JsonStudentWriter（与 roster 1.1 保持一致，使用初始化器格式）
        writer_path = provider_dir / "JsonStudentWriter.cs"
        writer_content = '''
public class JsonStudentWriter
{
    public void Write()
    {
        var roster = new RosterFile
        {
            Version = "1.1",
            Students = new List<Student>()
        };
    }
}
'''
        writer_path.write_text(writer_content, encoding="utf-8")

        # Strategy manifests
        for sid in ["FixedSeat", "RandomFill", "Defrag"]:
            make_manifest_json(manifest_dir / f"{sid}.json", "1.0.0", "1.0")

        # StrategyManifestProvider
        provider_code = '''
public class StrategyManifestProvider
{
    public const string MaxManifestVersion = "1.0";
}
'''
        (service_dir / "StrategyManifestProvider.cs").write_text(provider_code, encoding="utf-8")

        # onboarding_config.json
        make_onboarding_json(about_dir / "onboarding_config.json", "3.0")

        self.mgr = VersionManager(self.root)

    def tearDown(self):
        if self.tmpdir.exists():
            shutil.rmtree(self.tmpdir)

    # ── show / check ──

    def test_show(self):
        self.mgr.show()

    def test_check_all_pass(self):
        issues = self.mgr.check()
        self.assertEqual(issues, [])

    def test_check_detects_model_mismatch(self):
        path = self.root / "SeatFlow.Core/Models/StrategyConfig.cs"
        path.write_text('public string Version { get; set; } = "9.9";', encoding="utf-8")

        issues = self.mgr.check()
        errors = [i for i in issues if i["level"] == "ERROR" and "strategyConfig" in i["msg"]]
        self.assertTrue(len(errors) > 0, f"应检测到 Model 类版本与 file_versions.json 不一致")

    # ── bump-app ──

    def test_bump_app_patch(self):
        changes = self.mgr.bump_app(level="patch")
        self.assertEqual(changes["version.json"]["from"], "1.2.0")
        self.assertEqual(changes["version.json"]["to"], "1.2.1")

    def test_bump_app_set(self):
        changes = self.mgr.bump_app(set_to="2.0.0")
        self.assertEqual(changes["version.json"]["to"], "2.0.0")

    def test_bump_app_apply(self):
        changes = self.mgr.bump_app(level="patch")
        self.mgr.apply_changes(changes)
        data = json.loads(self.mgr.version_json_path.read_text(encoding="utf-8"))
        self.assertEqual(data["version"], "1.2.1")

    # ── bump-file ──

    def test_bump_file_changes_json_and_model(self):
        changes = self.mgr.bump_file("roster", set_to="1.2")
        # file_versions.json 中 roster 从 1.1 → 1.2
        fv_rel = str(self.mgr.file_versions_path.relative_to(self.root))
        self.assertIn(fv_rel, changes)
        # Model 类也应有变更
        model_rel = str((self.mgr.models_dir / "RosterFile.cs").relative_to(self.root))
        self.assertIn(model_rel, changes)

    def test_bump_file_apply(self):
        changes = self.mgr.bump_file("roster", set_to="1.2")
        self.mgr.apply_changes(changes)

        # 验证 file_versions.json
        fv = self.mgr.get_file_versions()
        self.assertEqual(fv["roster"], "1.2")

        # 验证 Model 类
        model_vers = self.mgr.get_model_versions()
        self.assertEqual(model_vers["roster"], "1.2")

        # 验证一致性
        issues = self.mgr.check()
        roster_errors = [i for i in issues if i["level"] == "ERROR" and "roster" in i["msg"]]
        self.assertEqual(roster_errors, [])

    def test_bump_file_invalid_type(self):
        with self.assertRaises(ValueError):
            self.mgr.bump_file("nonexistent", set_to="1.0")

    # ── bump-strategy ──

    def test_bump_strategy_single(self):
        changes = self.mgr.bump_strategy("FixedSeat", set_to="1.1.0")
        mf_rel = [k for k in changes if "FixedSeat" in k and not k.startswith("_")]
        self.assertTrue(len(mf_rel) > 0)

    def test_bump_strategy_apply(self):
        changes = self.mgr.bump_strategy("FixedSeat", set_to="1.1.0")
        self.mgr.apply_changes(changes)

        sv = self.mgr.get_strategy_versions()
        self.assertEqual(sv["FixedSeat"]["version"], "1.1.0")

    def test_bump_strategy_nonexistent(self):
        with self.assertRaises(ValueError):
            self.mgr.bump_strategy("NonExistent", set_to="1.0.0")

    # ── bump-onboarding ──

    def test_bump_onboarding_apply(self):
        changes = self.mgr.bump_onboarding(set_to="4.0")
        self.mgr.apply_changes(changes)
        self.assertEqual(self.mgr.get_onboarding_version(), "4.0")

    # ── sync ──

    def test_sync_models(self):
        # 故意让 Model 类版本落后
        path = self.root / "SeatFlow.Core/Models/VenueFile.cs"
        path.write_text('public string Version { get; set; } = "1.0";', encoding="utf-8")

        changes = self.mgr.sync_models()
        self.assertIn(str(path.relative_to(self.root)), changes)

        self.mgr.apply_changes(changes)
        issues = self.mgr.check()
        errors = [i for i in issues if i["level"] == "ERROR"]
        self.assertEqual(errors, [], f"sync 后不应有错误: {errors}")

    def test_sync_all_in_sync(self):
        changes = self.mgr.sync_models()
        self.assertEqual(changes, {})  # 无变更

    # ── backup ──

    def test_backup_creates_files(self):
        self.mgr.backup()
        backups = list((self.root / ".version-backups").glob("*"))
        self.assertTrue(len(backups) > 0, "备份应创建文件")


if __name__ == "__main__":
    unittest.main(verbosity=2)
