"""Tests for Agent Behavioral Fingerprinting."""

from __future__ import annotations

import json
import math
import random

import pytest

from replication.fingerprint import (
    AgentObservation,
    AlertSeverity,
    AlertType,
    BehavioralFingerprint,
    BehavioralFingerprinter,
    ComparisonResult,
    DriftEvent,
    FingerprintAlert,
    FingerprintConfig,
    FleetReport,
    ObservationType,
    Verdict,
    _cosine_similarity,
    _distribution_similarity,
    _shannon_entropy,
)


# ── Helpers ──


def _make_obs(
    obs_type: ObservationType = ObservationType.ACTION,
    value: str = "replicate",
    timestamp: float = 0.0,
    numeric: float | None = None,
) -> AgentObservation:
    return AgentObservation(
        obs_type=obs_type, value=value, timestamp=timestamp, numeric=numeric
    )


def _seed_agent(
    fp: BehavioralFingerprinter,
    agent_id: str,
    actions: list[str] | None = None,
    count: int = 20,
    rng: random.Random | None = None,
) -> None:
    """Seed an agent with varied observations."""
    rng = rng or random.Random(42)
    actions = actions or ["replicate", "communicate", "compute"]
    t = 1000.0
    for _ in range(count):
        otype = rng.choice(list(ObservationType))
        if otype == ObservationType.ACTION:
            obs = _make_obs(otype, rng.choice(actions), t)
        elif otype == ObservationType.RESOURCE_USE:
            obs = _make_obs(otype, "cpu", t, rng.gauss(0.5, 0.1))
        elif otype == ObservationType.TOOL_USE:
            obs = _make_obs(otype, rng.choice(["api", "file", "net"]), t)
        elif otype == ObservationType.COMMUNICATION:
            obs = _make_obs(otype, f"target-{rng.randint(1,3)}", t)
        elif otype == ObservationType.ERROR:
            obs = _make_obs(otype, "timeout", t)
        else:
            obs = _make_obs(otype, "data_read", t)
        fp.observe(agent_id, obs)
        t += rng.uniform(0.5, 5.0)


# ── Utility function tests ──


class TestUtilities:
    def test_shannon_entropy_uniform(self):
        dist = {"a": 0.25, "b": 0.25, "c": 0.25, "d": 0.25}
        assert abs(_shannon_entropy(dist) - 2.0) < 1e-9

    def test_shannon_entropy_certain(self):
        assert _shannon_entropy({"a": 1.0}) == 0.0

    def test_shannon_entropy_empty(self):
        assert _shannon_entropy({}) == 0.0

    def test_cosine_similarity_identical(self):
        v = [1.0, 2.0, 3.0]
        assert abs(_cosine_similarity(v, v) - 1.0) < 1e-9

    def test_cosine_similarity_orthogonal(self):
        assert abs(_cosine_similarity([1, 0], [0, 1])) < 1e-9

    def test_cosine_similarity_empty(self):
        assert _cosine_similarity([], []) == 0.0

    def test_cosine_similarity_zero_vector(self):
        assert _cosine_similarity([0, 0], [1, 2]) == 0.0

    def test_distribution_similarity_identical(self):
        d = {"a": 0.5, "b": 0.5}
        assert abs(_distribution_similarity(d, d) - 1.0) < 1e-9

    def test_distribution_similarity_disjoint(self):
        a = {"x": 1.0}
        b = {"y": 1.0}
        sim = _distribution_similarity(a, b)
        assert sim < 0.5

    def test_distribution_similarity_empty(self):
        assert _distribution_similarity({}, {}) == 1.0


# ── Observation & basic fingerprinting ──


class TestObservation:
    def test_create_observation(self):
        obs = _make_obs(ObservationType.ACTION, "replicate", 100.0)
        assert obs.obs_type == ObservationType.ACTION
        assert obs.value == "replicate"
        assert obs.timestamp == 100.0

    def test_observation_metadata(self):
        obs = AgentObservation(
            obs_type=ObservationType.ACTION,
            value="test",
            metadata={"key": "val"},
        )
        assert obs.metadata["key"] == "val"


class TestFingerprinter:
    def test_empty_fingerprint(self):
        fp = BehavioralFingerprinter()
        result = fp.build_fingerprint("unknown")
        assert result.observation_count == 0
        assert result.signature_hash == ""

    def test_single_observation(self):
        fp = BehavioralFingerprinter()
        fp.observe("a1", _make_obs(ObservationType.ACTION, "replicate", 1.0))
        result = fp.build_fingerprint("a1")
        assert result.observation_count == 1
        assert result.action_profile.top_action == "replicate"

    def test_agents_property(self):
        fp = BehavioralFingerprinter()
        fp.observe("b", _make_obs())
        fp.observe("a", _make_obs())
        assert fp.agents == ["a", "b"]

    def test_observation_count(self):
        fp = BehavioralFingerprinter()
        fp.observe("a", _make_obs())
        fp.observe("a", _make_obs())
        fp.observe("b", _make_obs())
        assert fp.observation_count == 3

    def test_observe_batch(self):
        fp = BehavioralFingerprinter()
        obs = [_make_obs(timestamp=float(i)) for i in range(5)]
        fp.observe_batch("a", obs)
        assert len(fp.get_observations("a")) == 5

    def test_fingerprint_caching(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        f1 = fp.build_fingerprint("a1")
        f2 = fp.build_fingerprint("a1")
        assert f1 is f2  # Same object = cached

    def test_cache_invalidation(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        f1 = fp.build_fingerprint("a1")
        fp.observe("a1", _make_obs())
        f2 = fp.build_fingerprint("a1")
        assert f1 is not f2

    def test_clear_agent(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        fp.clear("a1")
        assert "a1" not in fp.agents

    def test_clear_all(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2", rng=random.Random(99))
        fp.clear()
        assert fp.agents == []

    def test_action_profile(self):
        fp = BehavioralFingerprinter()
        for val in ["a", "a", "b"]:
            fp.observe("x", _make_obs(ObservationType.ACTION, val, 1.0))
        result = fp.build_fingerprint("x")
        assert result.action_profile.unique_actions == 2
        assert result.action_profile.total_actions == 3
        assert result.action_profile.top_action == "a"
        assert abs(result.action_profile.distribution["a"] - 2 / 3) < 1e-9

    def test_timing_profile(self):
        fp = BehavioralFingerprinter()
        for t in [1.0, 2.0, 3.0, 4.0, 5.0]:
            fp.observe("x", _make_obs(timestamp=t))
        result = fp.build_fingerprint("x")
        assert abs(result.timing_profile.mean_interval - 1.0) < 1e-9
        assert result.timing_profile.std_interval == 0.0

    def test_resource_profile(self):
        fp = BehavioralFingerprinter()
        for v in [0.4, 0.5, 0.6]:
            fp.observe(
                "x",
                _make_obs(ObservationType.RESOURCE_USE, "cpu", numeric=v),
            )
        result = fp.build_fingerprint("x")
        assert abs(result.resource_profile.mean_values["cpu"] - 0.5) < 1e-9

    def test_tool_distribution(self):
        fp = BehavioralFingerprinter()
        fp.observe("x", _make_obs(ObservationType.TOOL_USE, "api"))
        fp.observe("x", _make_obs(ObservationType.TOOL_USE, "api"))
        fp.observe("x", _make_obs(ObservationType.TOOL_USE, "file"))
        result = fp.build_fingerprint("x")
        assert abs(result.tool_distribution["api"] - 2 / 3) < 1e-9

    def test_error_rate(self):
        fp = BehavioralFingerprinter()
        fp.observe("x", _make_obs(ObservationType.ACTION, "do"))
        fp.observe("x", _make_obs(ObservationType.ERROR, "fail"))
        result = fp.build_fingerprint("x")
        assert abs(result.error_rate - 0.5) < 1e-9

    def test_communication_targets(self):
        fp = BehavioralFingerprinter()
        fp.observe("x", _make_obs(ObservationType.COMMUNICATION, "t1"))
        fp.observe("x", _make_obs(ObservationType.COMMUNICATION, "t1"))
        fp.observe("x", _make_obs(ObservationType.COMMUNICATION, "t2"))
        result = fp.build_fingerprint("x")
        assert abs(result.communication_targets["t1"] - 2 / 3) < 1e-9

    def test_signature_hash_deterministic(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        h1 = fp.build_fingerprint("a1").signature_hash
        fp2 = BehavioralFingerprinter()
        _seed_agent(fp2, "a1")
        h2 = fp2.build_fingerprint("a1").signature_hash
        assert h1 == h2

    def test_signature_hash_differs(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1", actions=["x", "y"])
        _seed_agent(fp, "a2", actions=["p", "q"], rng=random.Random(99))
        h1 = fp.build_fingerprint("a1").signature_hash
        h2 = fp.build_fingerprint("a2").signature_hash
        assert h1 != h2

    def test_to_dict(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        d = fp.build_fingerprint("a1").to_dict()
        assert d["agent_id"] == "a1"
        assert "signature_hash" in d

    def test_feature_vector_length(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        vec = fp.build_fingerprint("a1").feature_vector
        assert len(vec) == 15  # 2 + 5 + 4 + 3 + 1(error_rate)

    def test_first_last_seen(self):
        fp = BehavioralFingerprinter()
        fp.observe("x", _make_obs(timestamp=10.0))
        fp.observe("x", _make_obs(timestamp=20.0))
        fp.observe("x", _make_obs(timestamp=5.0))
        result = fp.build_fingerprint("x")
        assert result.first_seen == 5.0
        assert result.last_seen == 20.0


# ── Comparison tests ──


class TestComparison:
    def test_identical_agents(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")  # Same seed = same behavior
        cmp = fp.compare("a1", "a2")
        assert cmp.overall_similarity > 0.9
        assert cmp.verdict in (Verdict.SUSPICIOUS_CLONE, Verdict.LIKELY_IMPERSONATION)

    def test_different_agents(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1", actions=["x", "y", "z"])
        _seed_agent(fp, "a2", actions=["p", "q", "r"], rng=random.Random(99))
        cmp = fp.compare("a1", "a2")
        assert cmp.overall_similarity < 0.9
        assert cmp.verdict in (Verdict.DISTINCT, Verdict.SIMILAR)

    def test_comparison_to_dict(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2", rng=random.Random(99))
        cmp = fp.compare("a1", "a2")
        d = cmp.to_dict()
        assert "overall_similarity" in d
        assert "verdict" in d

    def test_comparison_dimension_scores(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")
        cmp = fp.compare("a1", "a2")
        assert "action" in cmp.dimension_scores
        assert "timing" in cmp.dimension_scores

    def test_comparison_explanation(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")
        cmp = fp.compare("a1", "a2")
        assert len(cmp.explanation) > 0

    def test_self_comparison(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        cmp = fp.compare("a1", "a1")
        assert abs(cmp.overall_similarity - 1.0) < 0.01


# ── Drift detection ──


class TestDrift:
    def test_no_drift_insufficient_data(self):
        fp = BehavioralFingerprinter(FingerprintConfig(drift_window=10))
        _seed_agent(fp, "a1", count=15)  # Less than 2*window
        assert fp.detect_drift("a1") is None

    def test_drift_detected(self):
        cfg = FingerprintConfig(drift_window=25, drift_threshold=0.2)
        fp = BehavioralFingerprinter(cfg)
        # First half: normal actions
        t = 0.0
        for _ in range(30):
            fp.observe("a1", _make_obs(ObservationType.ACTION, "compute", t))
            t += 1.0
        # Second half: completely different actions
        for _ in range(30):
            fp.observe("a1", _make_obs(ObservationType.ACTION, "escalate", t))
            t += 0.1  # Much faster timing too
        drift = fp.detect_drift("a1")
        assert drift is not None
        assert drift.agent_id == "a1"
        assert len(drift.changed_dimensions) > 0

    def test_no_drift_stable(self):
        cfg = FingerprintConfig(drift_window=20)
        fp = BehavioralFingerprinter(cfg)
        rng = random.Random(42)
        t = 0.0
        for _ in range(60):
            fp.observe(
                "a1",
                _make_obs(
                    ObservationType.ACTION,
                    rng.choice(["a", "b", "c"]),
                    t,
                ),
            )
            t += rng.uniform(0.5, 1.5)
        drift = fp.detect_drift("a1")
        # Stable behavior should not trigger drift (or very mild)
        if drift:
            assert drift.drift_score < 0.5


# ── Fleet analysis ──


class TestFleetAnalysis:
    def test_fleet_empty(self):
        fp = BehavioralFingerprinter()
        report = fp.analyze_fleet()
        assert report.total_agents == 0
        assert report.risk_grade == "A"

    def test_fleet_insufficient_data(self):
        fp = BehavioralFingerprinter(FingerprintConfig(min_observations=100))
        _seed_agent(fp, "a1", count=5)
        report = fp.analyze_fleet()
        assert report.insufficient_data == 1
        assert report.fingerprinted == 0

    def test_fleet_with_clone(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")  # Clone (same seed)
        _seed_agent(fp, "a3", actions=["x", "y"], rng=random.Random(99))
        report = fp.analyze_fleet()
        assert report.fingerprinted == 3
        assert len(report.alerts) > 0
        # Should find clone group
        assert len(report.clone_groups) > 0

    def test_fleet_distinct_agents(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1", actions=["x"], rng=random.Random(1))
        _seed_agent(fp, "a2", actions=["y"], rng=random.Random(2))
        _seed_agent(fp, "a3", actions=["z"], rng=random.Random(3))
        report = fp.analyze_fleet()
        assert len(report.clone_groups) == 0

    def test_fleet_report_render(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")
        report = fp.analyze_fleet()
        text = report.render()
        assert "Agent Behavioral Fingerprint Report" in text

    def test_fleet_report_json(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        report = fp.analyze_fleet()
        data = json.loads(report.to_json())
        assert "risk_score" in data
        assert "risk_grade" in data

    def test_fleet_report_to_dict(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        d = fp.analyze_fleet().to_dict()
        assert isinstance(d["total_agents"], int)

    def test_similarity_matrix(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2", rng=random.Random(99))
        report = fp.analyze_fleet()
        assert "a1" in report.similarity_matrix
        assert "a2" in report.similarity_matrix["a1"]

    def test_similarity_matrix_symmetric(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2", rng=random.Random(99))
        report = fp.analyze_fleet()
        assert abs(
            report.similarity_matrix["a1"]["a2"]
            - report.similarity_matrix["a2"]["a1"]
        ) < 1e-9

    def test_risk_grading(self):
        fp = BehavioralFingerprinter()
        assert fp._risk_grade(5) == "A"
        assert fp._risk_grade(20) == "B"
        assert fp._risk_grade(40) == "C"
        assert fp._risk_grade(60) == "D"
        assert fp._risk_grade(90) == "F"


# ── Config tests ──


class TestConfig:
    def test_default_config(self):
        cfg = FingerprintConfig()
        assert cfg.min_observations == 10
        assert cfg.clone_threshold == 0.90

    def test_custom_thresholds(self):
        cfg = FingerprintConfig(clone_threshold=0.95, suspicious_threshold=0.85)
        fp = BehavioralFingerprinter(cfg)
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")
        cmp = fp.compare("a1", "a2")
        assert cmp.overall_similarity > 0


# ── Alert tests ──


class TestAlerts:
    def test_alert_to_dict(self):
        alert = FingerprintAlert(
            alert_type=AlertType.CLONE_DETECTED,
            severity=AlertSeverity.HIGH,
            agents=["a1", "a2"],
            similarity=0.95,
            description="Clone detected",
        )
        d = alert.to_dict()
        assert d["type"] == "clone_detected"
        assert d["severity"] == "high"

    def test_low_entropy_anomaly(self):
        fp = BehavioralFingerprinter(FingerprintConfig(min_observations=5))
        # Agent with 3+ unique actions but dominated by one → low entropy
        for i in range(50):
            fp.observe("x", _make_obs(ObservationType.ACTION, "a", float(i)))
        fp.observe("x", _make_obs(ObservationType.ACTION, "b", 50.0))
        fp.observe("x", _make_obs(ObservationType.ACTION, "c", 51.0))
        result = fp.build_fingerprint("x")
        assert result.action_profile.entropy < 0.5
        assert result.action_profile.unique_actions >= 3
        report = fp.analyze_fleet()
        anomaly_alerts = [
            a for a in report.alerts if a.alert_type == AlertType.BEHAVIORAL_ANOMALY
        ]
        assert len(anomaly_alerts) > 0


# ── Enum coverage ──


class TestEnums:
    def test_observation_types(self):
        assert len(ObservationType) == 7

    def test_verdict_values(self):
        assert Verdict.DISTINCT.value == "distinct"
        assert Verdict.LIKELY_IMPERSONATION.value == "likely_impersonation"

    def test_alert_severity(self):
        assert AlertSeverity.CRITICAL.value == "critical"

    def test_alert_type(self):
        assert AlertType.SUDDEN_CHANGE.value == "sudden_change"


# ── Edge cases ──


class TestEdgeCases:
    def test_compare_empty_agents(self):
        fp = BehavioralFingerprinter()
        cmp = fp.compare("a1", "a2")
        # Empty agents have identical empty distributions → high similarity
        assert cmp.overall_similarity >= 0

    def test_single_obs_fingerprint(self):
        fp = BehavioralFingerprinter()
        fp.observe("a1", _make_obs(ObservationType.ERROR, "crash", 1.0))
        result = fp.build_fingerprint("a1")
        assert result.error_rate == 1.0

    def test_no_timestamp_obs(self):
        fp = BehavioralFingerprinter()
        fp.observe("a1", _make_obs(timestamp=0.0))
        result = fp.build_fingerprint("a1")
        assert result.first_seen == 0.0

    def test_data_access_obs(self):
        fp = BehavioralFingerprinter()
        fp.observe("a1", _make_obs(ObservationType.DATA_ACCESS, "secret_file"))
        result = fp.build_fingerprint("a1")
        assert result.observation_count == 1

    def test_get_observations_unknown_agent(self):
        fp = BehavioralFingerprinter()
        assert fp.get_observations("nonexistent") == []

    def test_burst_and_idle_ratios(self):
        fp = BehavioralFingerprinter()
        times = [0, 0.01, 0.02, 10.0, 10.01, 10.02, 100.0]
        for t in times:
            fp.observe("x", _make_obs(timestamp=t))
        result = fp.build_fingerprint("x")
        tp = result.timing_profile
        assert tp.mean_interval >= 0

    def test_cosine_similarity_mismatched_length(self):
        assert _cosine_similarity([1, 2], [1, 2, 3]) == 0.0

    def test_resource_similarity_no_overlap(self):
        """Two agents with different resource types should still get a score."""
        fp = BehavioralFingerprinter()
        fp.observe("a", _make_obs(ObservationType.RESOURCE_USE, "cpu", numeric=0.5))
        fp.observe("b", _make_obs(ObservationType.RESOURCE_USE, "gpu", numeric=0.8))
        _seed_agent(fp, "a", count=15)
        _seed_agent(fp, "b", count=15, rng=random.Random(99))
        cmp = fp.compare("a", "b")
        assert 0.0 <= cmp.resource_similarity <= 1.0

    def test_error_distribution_multiple_types(self):
        fp = BehavioralFingerprinter()
        fp.observe("x", _make_obs(ObservationType.ERROR, "timeout", 1.0))
        fp.observe("x", _make_obs(ObservationType.ERROR, "timeout", 2.0))
        fp.observe("x", _make_obs(ObservationType.ERROR, "oom", 3.0))
        fp.observe("x", _make_obs(ObservationType.ACTION, "ok", 4.0))
        result = fp.build_fingerprint("x")
        assert abs(result.error_distribution["timeout"] - 2 / 3) < 1e-9
        assert abs(result.error_distribution["oom"] - 1 / 3) < 1e-9
        assert abs(result.error_rate - 0.75) < 1e-9


# ── _compute_stats tests ──


class TestComputeStats:
    def test_empty(self):
        from replication.fingerprint import _compute_stats
        m, s, mn, mx = _compute_stats([])
        assert m == 0.0 and s == 0.0

    def test_single_value(self):
        from replication.fingerprint import _compute_stats
        m, s, mn, mx = _compute_stats([5.0])
        assert m == 5.0
        assert mn == 5.0
        assert mx == 5.0

    def test_multiple_values(self):
        from replication.fingerprint import _compute_stats
        m, s, mn, mx = _compute_stats([2.0, 4.0, 6.0])
        assert abs(m - 4.0) < 1e-9
        assert mn == 2.0
        assert mx == 6.0
        assert s > 0


# ── DriftEvent dataclass ──


class TestDriftEvent:
    def test_drift_event_fields(self):
        de = DriftEvent(
            agent_id="a1",
            drift_score=0.45,
            changed_dimensions=["action", "timing"],
            timestamp=1000.0,
            description="test drift",
        )
        assert de.agent_id == "a1"
        assert de.drift_score == 0.45
        assert "action" in de.changed_dimensions


# ── Fleet risk score edge cases ──


class TestRiskScoring:
    def test_risk_score_caps_at_100(self):
        """Many critical alerts should not exceed 100."""
        fp = BehavioralFingerprinter(FingerprintConfig(min_observations=3))
        # Create many clone pairs
        for i in range(10):
            _seed_agent(fp, f"clone-{i}", count=10)
        report = fp.analyze_fleet()
        assert report.risk_score <= 100.0

    def test_risk_grade_boundaries(self):
        assert BehavioralFingerprinter._risk_grade(0) == "A"
        assert BehavioralFingerprinter._risk_grade(10) == "A"
        assert BehavioralFingerprinter._risk_grade(10.1) == "B"
        assert BehavioralFingerprinter._risk_grade(25) == "B"
        assert BehavioralFingerprinter._risk_grade(25.1) == "C"
        assert BehavioralFingerprinter._risk_grade(50) == "C"
        assert BehavioralFingerprinter._risk_grade(50.1) == "D"
        assert BehavioralFingerprinter._risk_grade(75) == "D"
        assert BehavioralFingerprinter._risk_grade(75.1) == "F"


# ── Fleet report rendering ──


class TestFleetReportRendering:
    def test_render_with_clone_groups(self):
        fp = BehavioralFingerprinter()
        _seed_agent(fp, "a1")
        _seed_agent(fp, "a2")  # Clone
        report = fp.analyze_fleet()
        text = report.render()
        assert "Clone Groups" in text

    def test_render_with_drift(self):
        """Fleet report should include drift events section when drift occurs."""
        cfg = FingerprintConfig(drift_window=15, drift_threshold=0.15)
        fp = BehavioralFingerprinter(cfg)
        t = 0.0
        for _ in range(20):
            fp.observe("a1", _make_obs(ObservationType.ACTION, "compute", t))
            t += 1.0
        for _ in range(20):
            fp.observe("a1", _make_obs(ObservationType.ACTION, "escalate", t))
            t += 0.05
        report = fp.analyze_fleet()
        if report.drift_events:
            text = report.render()
            assert "Drift Events" in text


# ── CLI / demo ──


class TestCLI:
    def test_demo_runs(self, capsys):
        from replication.fingerprint import _demo
        _demo(num_agents=3, num_obs=30)
        captured = capsys.readouterr()
        assert "Agent Behavioral Fingerprint Report" in captured.out

    def test_demo_json(self, capsys):
        from replication.fingerprint import _demo
        _demo(num_agents=3, num_obs=30, as_json=True)
        captured = capsys.readouterr()
        data = json.loads(captured.out)
        assert "risk_score" in data
