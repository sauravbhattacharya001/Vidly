"""Agent Behavioral Fingerprinting — unique behavioral signatures per agent.

Creates composite behavioral fingerprints from observed agent actions to
detect impersonation, identity swaps, and cloned agents masquerading as
others.  Each agent develops a unique "behavioral DNA" over time based on
their action patterns, timing distributions, resource usage, vocabulary,
and interaction styles.

Example::

    from replication.fingerprint import (
        BehavioralFingerprinter, FingerprintConfig, AgentObservation,
        ObservationType,
    )

    fp = BehavioralFingerprinter()

    # Feed observations
    fp.observe("agent-1", AgentObservation(
        obs_type=ObservationType.ACTION,
        value="replicate",
        timestamp=1000.0,
    ))
    fp.observe("agent-1", AgentObservation(
        obs_type=ObservationType.RESOURCE_USE,
        value="cpu",
        numeric=0.45,
        timestamp=1001.0,
    ))

    # Build fingerprint
    profile = fp.build_fingerprint("agent-1")
    print(f"Entropy: {profile.action_entropy:.3f}")
    print(f"Signature hash: {profile.signature_hash}")

    # Compare two agents
    sim = fp.compare("agent-1", "agent-2")
    print(f"Similarity: {sim.overall_similarity:.2%}")
    print(f"Verdict: {sim.verdict}")

    # Detect impersonation across fleet
    report = fp.analyze_fleet()
    for alert in report.alerts:
        print(f"⚠ {alert.description}")

CLI::

    python -m replication.fingerprint
    python -m replication.fingerprint --agents 10 --observations 200
    python -m replication.fingerprint --json
"""

from __future__ import annotations

import hashlib
import json
import math
import sys
import time
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional, Sequence, Set, Tuple


# ── Enums ──


class ObservationType(Enum):
    """Types of observable agent behaviors."""

    ACTION = "action"
    RESOURCE_USE = "resource_use"
    COMMUNICATION = "communication"
    TIMING = "timing"
    ERROR = "error"
    TOOL_USE = "tool_use"
    DATA_ACCESS = "data_access"


class Verdict(Enum):
    """Comparison verdict between two agents."""

    DISTINCT = "distinct"
    SIMILAR = "similar"
    SUSPICIOUS_CLONE = "suspicious_clone"
    LIKELY_IMPERSONATION = "likely_impersonation"


class AlertSeverity(Enum):
    """Alert severity levels."""

    INFO = "info"
    WARNING = "warning"
    HIGH = "high"
    CRITICAL = "critical"


class AlertType(Enum):
    """Types of fingerprinting alerts."""

    CLONE_DETECTED = "clone_detected"
    IDENTITY_DRIFT = "identity_drift"
    BEHAVIORAL_ANOMALY = "behavioral_anomaly"
    TIMING_MISMATCH = "timing_mismatch"
    IMPERSONATION_ATTEMPT = "impersonation_attempt"
    SUDDEN_CHANGE = "sudden_change"


# ── Data Classes ──


@dataclass
class AgentObservation:
    """A single observed agent behavior."""

    obs_type: ObservationType
    value: str
    timestamp: float = 0.0
    numeric: Optional[float] = None
    metadata: Dict[str, Any] = field(default_factory=dict)


@dataclass
class FingerprintConfig:
    """Configuration for behavioral fingerprinting."""

    # Minimum observations to build a fingerprint
    min_observations: int = 10
    # Similarity thresholds
    clone_threshold: float = 0.90
    suspicious_threshold: float = 0.80
    # Weight for each dimension
    action_weight: float = 0.25
    timing_weight: float = 0.20
    resource_weight: float = 0.20
    communication_weight: float = 0.15
    tool_weight: float = 0.10
    error_weight: float = 0.10
    # Sliding window for drift detection (number of observations)
    drift_window: int = 50
    # Drift threshold (cosine distance)
    drift_threshold: float = 0.30


@dataclass
class TimingProfile:
    """Timing behavior profile for an agent."""

    mean_interval: float = 0.0
    std_interval: float = 0.0
    min_interval: float = 0.0
    max_interval: float = 0.0
    burst_ratio: float = 0.0  # fraction of intervals below mean/3
    idle_ratio: float = 0.0  # fraction of intervals above mean*3


@dataclass
class ActionProfile:
    """Action distribution profile."""

    distribution: Dict[str, float] = field(default_factory=dict)
    entropy: float = 0.0
    top_action: str = ""
    unique_actions: int = 0
    total_actions: int = 0


@dataclass
class ResourceProfile:
    """Resource usage profile."""

    mean_values: Dict[str, float] = field(default_factory=dict)
    std_values: Dict[str, float] = field(default_factory=dict)
    resource_types: List[str] = field(default_factory=list)


@dataclass
class BehavioralFingerprint:
    """Complete behavioral fingerprint for an agent."""

    agent_id: str
    observation_count: int = 0
    first_seen: float = 0.0
    last_seen: float = 0.0
    action_profile: ActionProfile = field(default_factory=ActionProfile)
    timing_profile: TimingProfile = field(default_factory=TimingProfile)
    resource_profile: ResourceProfile = field(default_factory=ResourceProfile)
    communication_targets: Dict[str, int] = field(default_factory=dict)
    tool_distribution: Dict[str, float] = field(default_factory=dict)
    error_rate: float = 0.0
    error_distribution: Dict[str, float] = field(default_factory=dict)
    signature_hash: str = ""
    feature_vector: List[float] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "agent_id": self.agent_id,
            "observation_count": self.observation_count,
            "first_seen": self.first_seen,
            "last_seen": self.last_seen,
            "action_entropy": self.action_profile.entropy,
            "top_action": self.action_profile.top_action,
            "unique_actions": self.action_profile.unique_actions,
            "mean_interval": self.timing_profile.mean_interval,
            "burst_ratio": self.timing_profile.burst_ratio,
            "error_rate": self.error_rate,
            "signature_hash": self.signature_hash,
        }


@dataclass
class ComparisonResult:
    """Result of comparing two agent fingerprints."""

    agent_a: str
    agent_b: str
    overall_similarity: float = 0.0
    action_similarity: float = 0.0
    timing_similarity: float = 0.0
    resource_similarity: float = 0.0
    communication_similarity: float = 0.0
    tool_similarity: float = 0.0
    error_similarity: float = 0.0
    verdict: Verdict = Verdict.DISTINCT
    dimension_scores: Dict[str, float] = field(default_factory=dict)
    explanation: str = ""

    def to_dict(self) -> Dict[str, Any]:
        return {
            "agent_a": self.agent_a,
            "agent_b": self.agent_b,
            "overall_similarity": round(self.overall_similarity, 4),
            "verdict": self.verdict.value,
            "dimension_scores": {
                k: round(v, 4) for k, v in self.dimension_scores.items()
            },
            "explanation": self.explanation,
        }


@dataclass
class FingerprintAlert:
    """Alert from fingerprint analysis."""

    alert_type: AlertType
    severity: AlertSeverity
    agents: List[str]
    similarity: float = 0.0
    description: str = ""
    details: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "type": self.alert_type.value,
            "severity": self.severity.value,
            "agents": self.agents,
            "similarity": round(self.similarity, 4),
            "description": self.description,
        }


@dataclass
class DriftEvent:
    """Behavioral drift detected for an agent."""

    agent_id: str
    drift_score: float
    changed_dimensions: List[str]
    timestamp: float = 0.0
    description: str = ""


@dataclass
class FleetReport:
    """Fleet-wide fingerprint analysis report."""

    total_agents: int = 0
    fingerprinted: int = 0
    insufficient_data: int = 0
    alerts: List[FingerprintAlert] = field(default_factory=list)
    clone_groups: List[List[str]] = field(default_factory=list)
    drift_events: List[DriftEvent] = field(default_factory=list)
    similarity_matrix: Dict[str, Dict[str, float]] = field(default_factory=dict)
    risk_score: float = 0.0
    risk_grade: str = "A"

    def to_dict(self) -> Dict[str, Any]:
        return {
            "total_agents": self.total_agents,
            "fingerprinted": self.fingerprinted,
            "insufficient_data": self.insufficient_data,
            "alert_count": len(self.alerts),
            "clone_groups": self.clone_groups,
            "drift_events": len(self.drift_events),
            "risk_score": round(self.risk_score, 1),
            "risk_grade": self.risk_grade,
            "alerts": [a.to_dict() for a in self.alerts],
        }

    def to_json(self, indent: int = 2) -> str:
        return json.dumps(self.to_dict(), indent=indent)

    def render(self) -> str:
        lines = [
            "╔══════════════════════════════════════════════════════════╗",
            "║          Agent Behavioral Fingerprint Report            ║",
            "╚══════════════════════════════════════════════════════════╝",
            "",
            f"  Agents analyzed:     {self.total_agents}",
            f"  Fingerprinted:       {self.fingerprinted}",
            f"  Insufficient data:   {self.insufficient_data}",
            f"  Risk score:          {self.risk_score:.1f}/100  [{self.risk_grade}]",
            "",
        ]

        if self.clone_groups:
            lines.append("  ┌─ Clone Groups ─────────────────────────────────┐")
            for i, group in enumerate(self.clone_groups, 1):
                lines.append(f"  │  Group {i}: {', '.join(group):<42}│")
            lines.append("  └────────────────────────────────────────────────┘")
            lines.append("")

        if self.alerts:
            lines.append("  ┌─ Alerts ───────────────────────────────────────┐")
            for alert in self.alerts:
                sev = alert.severity.value.upper()
                lines.append(f"  │  [{sev}] {alert.description[:44]:<44}│")
            lines.append("  └────────────────────────────────────────────────┘")
            lines.append("")

        if self.drift_events:
            lines.append("  ┌─ Drift Events ─────────────────────────────────┐")
            for de in self.drift_events:
                dims = ", ".join(de.changed_dimensions[:3])
                lines.append(
                    f"  │  {de.agent_id}: drift={de.drift_score:.2f} ({dims})"
                    .ljust(52) + "│"
                )
            lines.append("  └────────────────────────────────────────────────┘")

        return "\n".join(lines)


# ── Helper Functions ──


def _shannon_entropy(distribution: Dict[str, float]) -> float:
    """Compute Shannon entropy of a probability distribution."""
    entropy = 0.0
    for p in distribution.values():
        if p > 0:
            entropy -= p * math.log2(p)
    return entropy


def _cosine_similarity(a: List[float], b: List[float]) -> float:
    """Compute cosine similarity between two vectors."""
    if len(a) != len(b) or not a:
        return 0.0
    dot = sum(x * y for x, y in zip(a, b))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(x * x for x in b))
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return dot / (norm_a * norm_b)


def _distribution_similarity(
    a: Dict[str, float], b: Dict[str, float]
) -> float:
    """Compute similarity between two probability distributions (1 - Jensen-Shannon)."""
    all_keys = set(a.keys()) | set(b.keys())
    if not all_keys:
        return 1.0
    # Build aligned vectors
    va = [a.get(k, 0.0) for k in all_keys]
    vb = [b.get(k, 0.0) for k in all_keys]
    # Jensen-Shannon divergence
    m = [(x + y) / 2 for x, y in zip(va, vb)]
    jsd = 0.0
    for i in range(len(va)):
        if va[i] > 0 and m[i] > 0:
            jsd += va[i] * math.log2(va[i] / m[i])
        if vb[i] > 0 and m[i] > 0:
            jsd += vb[i] * math.log2(vb[i] / m[i])
    jsd /= 2
    # Clamp to [0, 1]
    jsd = max(0.0, min(1.0, jsd))
    return 1.0 - math.sqrt(jsd)


def _compute_stats(values: Sequence[float]) -> Tuple[float, float, float, float]:
    """Return (mean, std, min, max) for a sequence of floats."""
    if not values:
        return 0.0, 0.0, 0.0, 0.0
    n = len(values)
    mean = sum(values) / n
    variance = sum((x - mean) ** 2 for x in values) / n if n > 1 else 0.0
    return mean, math.sqrt(variance), min(values), max(values)


# ── Main Class ──


class BehavioralFingerprinter:
    """Builds and compares behavioral fingerprints for agents."""

    def __init__(self, config: Optional[FingerprintConfig] = None):
        self.config = config or FingerprintConfig()
        # agent_id -> list of observations
        self._observations: Dict[str, List[AgentObservation]] = defaultdict(list)
        # Cached fingerprints
        self._fingerprints: Dict[str, BehavioralFingerprint] = {}
        # Historical fingerprints for drift detection
        self._history: Dict[str, List[BehavioralFingerprint]] = defaultdict(list)

    @property
    def agents(self) -> List[str]:
        """Return list of observed agent IDs."""
        return sorted(self._observations.keys())

    @property
    def observation_count(self) -> int:
        """Total observations across all agents."""
        return sum(len(obs) for obs in self._observations.values())

    def observe(self, agent_id: str, observation: AgentObservation) -> None:
        """Record an observation for an agent."""
        self._observations[agent_id].append(observation)
        # Invalidate cached fingerprint
        self._fingerprints.pop(agent_id, None)

    def observe_batch(
        self, agent_id: str, observations: Sequence[AgentObservation]
    ) -> None:
        """Record multiple observations for an agent."""
        self._observations[agent_id].extend(observations)
        self._fingerprints.pop(agent_id, None)

    def build_fingerprint(self, agent_id: str) -> BehavioralFingerprint:
        """Build a behavioral fingerprint for an agent.

        Returns a cached result if observations haven't changed.
        """
        if agent_id in self._fingerprints:
            return self._fingerprints[agent_id]

        obs = self._observations.get(agent_id, [])
        fp = BehavioralFingerprint(agent_id=agent_id)
        fp.observation_count = len(obs)

        if not obs:
            self._fingerprints[agent_id] = fp
            return fp

        timestamps = [o.timestamp for o in obs if o.timestamp > 0]
        if timestamps:
            fp.first_seen = min(timestamps)
            fp.last_seen = max(timestamps)

        # Action profile
        fp.action_profile = self._build_action_profile(obs)

        # Timing profile
        fp.timing_profile = self._build_timing_profile(obs)

        # Resource profile
        fp.resource_profile = self._build_resource_profile(obs)

        # Communication targets
        comm_obs = [o for o in obs if o.obs_type == ObservationType.COMMUNICATION]
        if comm_obs:
            target_counts: Counter = Counter()
            for o in comm_obs:
                target_counts[o.value] += 1
            total = sum(target_counts.values())
            fp.communication_targets = {
                k: v / total for k, v in target_counts.items()
            }

        # Tool distribution
        tool_obs = [o for o in obs if o.obs_type == ObservationType.TOOL_USE]
        if tool_obs:
            tool_counts: Counter = Counter()
            for o in tool_obs:
                tool_counts[o.value] += 1
            total = sum(tool_counts.values())
            fp.tool_distribution = {k: v / total for k, v in tool_counts.items()}

        # Error rate and distribution
        error_obs = [o for o in obs if o.obs_type == ObservationType.ERROR]
        fp.error_rate = len(error_obs) / len(obs) if obs else 0.0
        if error_obs:
            err_counts: Counter = Counter()
            for o in error_obs:
                err_counts[o.value] += 1
            total = sum(err_counts.values())
            fp.error_distribution = {k: v / total for k, v in err_counts.items()}

        # Feature vector for similarity comparisons
        fp.feature_vector = self._build_feature_vector(fp)

        # Signature hash
        fp.signature_hash = self._compute_hash(fp)

        self._fingerprints[agent_id] = fp
        return fp

    def compare(self, agent_a: str, agent_b: str) -> ComparisonResult:
        """Compare fingerprints of two agents."""
        fp_a = self.build_fingerprint(agent_a)
        fp_b = self.build_fingerprint(agent_b)

        result = ComparisonResult(agent_a=agent_a, agent_b=agent_b)

        # Per-dimension similarity
        result.action_similarity = _distribution_similarity(
            fp_a.action_profile.distribution, fp_b.action_profile.distribution
        )
        result.timing_similarity = self._timing_similarity(
            fp_a.timing_profile, fp_b.timing_profile
        )
        result.resource_similarity = self._resource_similarity(
            fp_a.resource_profile, fp_b.resource_profile
        )
        result.communication_similarity = _distribution_similarity(
            fp_a.communication_targets, fp_b.communication_targets
        )
        result.tool_similarity = _distribution_similarity(
            fp_a.tool_distribution, fp_b.tool_distribution
        )
        result.error_similarity = _distribution_similarity(
            fp_a.error_distribution, fp_b.error_distribution
        )

        result.dimension_scores = {
            "action": result.action_similarity,
            "timing": result.timing_similarity,
            "resource": result.resource_similarity,
            "communication": result.communication_similarity,
            "tool": result.tool_similarity,
            "error": result.error_similarity,
        }

        # Weighted overall
        cfg = self.config
        result.overall_similarity = (
            cfg.action_weight * result.action_similarity
            + cfg.timing_weight * result.timing_similarity
            + cfg.resource_weight * result.resource_similarity
            + cfg.communication_weight * result.communication_similarity
            + cfg.tool_weight * result.tool_similarity
            + cfg.error_weight * result.error_similarity
        )

        # Verdict
        if result.overall_similarity >= cfg.clone_threshold:
            result.verdict = Verdict.LIKELY_IMPERSONATION
            result.explanation = (
                f"Agents {agent_a} and {agent_b} have {result.overall_similarity:.0%} "
                f"behavioral similarity — likely impersonation or cloning."
            )
        elif result.overall_similarity >= cfg.suspicious_threshold:
            result.verdict = Verdict.SUSPICIOUS_CLONE
            result.explanation = (
                f"Agents {agent_a} and {agent_b} show {result.overall_similarity:.0%} "
                f"similarity — suspiciously similar behavior."
            )
        elif result.overall_similarity >= 0.50:
            result.verdict = Verdict.SIMILAR
            result.explanation = (
                f"Agents share some behavioral patterns ({result.overall_similarity:.0%})."
            )
        else:
            result.verdict = Verdict.DISTINCT
            result.explanation = (
                f"Agents have distinct behavioral profiles ({result.overall_similarity:.0%})."
            )

        return result

    def detect_drift(self, agent_id: str) -> Optional[DriftEvent]:
        """Detect behavioral drift by comparing recent vs. historical behavior.

        Splits observations into two halves and compares fingerprints.
        """
        obs = self._observations.get(agent_id, [])
        window = self.config.drift_window
        if len(obs) < window * 2:
            return None

        # Build fingerprints for old window vs recent window
        old_obs = obs[-window * 2 : -window]
        new_obs = obs[-window:]

        old_fp = self._build_fingerprint_from_obs("_old", old_obs)
        new_fp = self._build_fingerprint_from_obs("_new", new_obs)

        # Compare per dimension
        dims: Dict[str, float] = {}
        dims["action"] = 1.0 - _distribution_similarity(
            old_fp.action_profile.distribution,
            new_fp.action_profile.distribution,
        )
        dims["timing"] = 1.0 - self._timing_similarity(
            old_fp.timing_profile, new_fp.timing_profile
        )
        dims["resource"] = 1.0 - self._resource_similarity(
            old_fp.resource_profile, new_fp.resource_profile
        )
        dims["tool"] = 1.0 - _distribution_similarity(
            old_fp.tool_distribution, new_fp.tool_distribution
        )
        dims["error"] = abs(old_fp.error_rate - new_fp.error_rate)

        drift_score = sum(dims.values()) / len(dims)
        changed = [d for d, v in dims.items() if v > self.config.drift_threshold]

        if not changed:
            return None

        event = DriftEvent(
            agent_id=agent_id,
            drift_score=drift_score,
            changed_dimensions=changed,
            timestamp=time.time(),
            description=(
                f"Agent {agent_id} behavioral drift detected "
                f"(score={drift_score:.2f}) in: {', '.join(changed)}"
            ),
        )
        return event

    def analyze_fleet(self) -> FleetReport:
        """Analyze all observed agents for clones, drift, and anomalies."""
        report = FleetReport()
        agents = self.agents
        report.total_agents = len(agents)

        # Build fingerprints
        fingerprints: Dict[str, BehavioralFingerprint] = {}
        for aid in agents:
            obs_count = len(self._observations.get(aid, []))
            if obs_count >= self.config.min_observations:
                fingerprints[aid] = self.build_fingerprint(aid)
                report.fingerprinted += 1
            else:
                report.insufficient_data += 1

        fp_agents = sorted(fingerprints.keys())

        # Pairwise comparisons
        report.similarity_matrix = {}
        clone_edges: List[Tuple[str, str, float]] = []

        for i, a in enumerate(fp_agents):
            report.similarity_matrix[a] = {}
            for j, b in enumerate(fp_agents):
                if i == j:
                    report.similarity_matrix[a][b] = 1.0
                    continue
                if j < i:
                    # Already computed
                    report.similarity_matrix[a][b] = report.similarity_matrix[b][a]
                    continue
                cmp = self.compare(a, b)
                report.similarity_matrix[a][b] = cmp.overall_similarity

                if cmp.verdict == Verdict.LIKELY_IMPERSONATION:
                    report.alerts.append(
                        FingerprintAlert(
                            alert_type=AlertType.IMPERSONATION_ATTEMPT,
                            severity=AlertSeverity.CRITICAL,
                            agents=[a, b],
                            similarity=cmp.overall_similarity,
                            description=cmp.explanation,
                        )
                    )
                    clone_edges.append((a, b, cmp.overall_similarity))
                elif cmp.verdict == Verdict.SUSPICIOUS_CLONE:
                    report.alerts.append(
                        FingerprintAlert(
                            alert_type=AlertType.CLONE_DETECTED,
                            severity=AlertSeverity.HIGH,
                            agents=[a, b],
                            similarity=cmp.overall_similarity,
                            description=cmp.explanation,
                        )
                    )
                    clone_edges.append((a, b, cmp.overall_similarity))

        # Group clones via union-find
        if clone_edges:
            report.clone_groups = self._group_clones(clone_edges, fp_agents)

        # Drift detection
        for aid in fp_agents:
            drift = self.detect_drift(aid)
            if drift:
                report.drift_events.append(drift)
                report.alerts.append(
                    FingerprintAlert(
                        alert_type=AlertType.IDENTITY_DRIFT,
                        severity=AlertSeverity.WARNING,
                        agents=[aid],
                        similarity=drift.drift_score,
                        description=drift.description,
                    )
                )

        # Anomaly detection — agents with very low entropy (robotic)
        for aid, fp in fingerprints.items():
            if (
                fp.action_profile.unique_actions >= 3
                and fp.action_profile.entropy < 0.5
            ):
                report.alerts.append(
                    FingerprintAlert(
                        alert_type=AlertType.BEHAVIORAL_ANOMALY,
                        severity=AlertSeverity.INFO,
                        agents=[aid],
                        description=(
                            f"Agent {aid} shows very low action entropy "
                            f"({fp.action_profile.entropy:.2f}) — highly repetitive."
                        ),
                    )
                )

        # Risk score
        report.risk_score = self._compute_risk_score(report)
        report.risk_grade = self._risk_grade(report.risk_score)

        return report

    def get_observations(self, agent_id: str) -> List[AgentObservation]:
        """Return observations for an agent."""
        return list(self._observations.get(agent_id, []))

    def clear(self, agent_id: Optional[str] = None) -> None:
        """Clear observations. If agent_id given, only that agent."""
        if agent_id:
            self._observations.pop(agent_id, None)
            self._fingerprints.pop(agent_id, None)
            self._history.pop(agent_id, None)
        else:
            self._observations.clear()
            self._fingerprints.clear()
            self._history.clear()

    # ── Internal Methods ──

    def _build_action_profile(
        self, obs: Sequence[AgentObservation]
    ) -> ActionProfile:
        action_obs = [o for o in obs if o.obs_type == ObservationType.ACTION]
        if not action_obs:
            return ActionProfile()

        counts: Counter = Counter()
        for o in action_obs:
            counts[o.value] += 1
        total = sum(counts.values())
        dist = {k: v / total for k, v in counts.items()}

        top = counts.most_common(1)[0][0] if counts else ""
        return ActionProfile(
            distribution=dist,
            entropy=_shannon_entropy(dist),
            top_action=top,
            unique_actions=len(counts),
            total_actions=total,
        )

    def _build_timing_profile(
        self, obs: Sequence[AgentObservation]
    ) -> TimingProfile:
        timestamps = sorted(o.timestamp for o in obs if o.timestamp > 0)
        if len(timestamps) < 2:
            return TimingProfile()

        intervals = [
            timestamps[i + 1] - timestamps[i]
            for i in range(len(timestamps) - 1)
        ]
        mean, std, mn, mx = _compute_stats(intervals)

        burst_count = sum(1 for iv in intervals if iv < mean / 3) if mean > 0 else 0
        idle_count = sum(1 for iv in intervals if iv > mean * 3) if mean > 0 else 0

        return TimingProfile(
            mean_interval=mean,
            std_interval=std,
            min_interval=mn,
            max_interval=mx,
            burst_ratio=burst_count / len(intervals) if intervals else 0.0,
            idle_ratio=idle_count / len(intervals) if intervals else 0.0,
        )

    def _build_resource_profile(
        self, obs: Sequence[AgentObservation]
    ) -> ResourceProfile:
        res_obs = [o for o in obs if o.obs_type == ObservationType.RESOURCE_USE]
        if not res_obs:
            return ResourceProfile()

        by_type: Dict[str, List[float]] = defaultdict(list)
        for o in res_obs:
            if o.numeric is not None:
                by_type[o.value].append(o.numeric)

        means: Dict[str, float] = {}
        stds: Dict[str, float] = {}
        for rtype, vals in by_type.items():
            m, s, _, _ = _compute_stats(vals)
            means[rtype] = m
            stds[rtype] = s

        return ResourceProfile(
            mean_values=means,
            std_values=stds,
            resource_types=sorted(by_type.keys()),
        )

    def _build_feature_vector(self, fp: BehavioralFingerprint) -> List[float]:
        """Build a numeric feature vector from the fingerprint."""
        vec: List[float] = []
        # Action features
        vec.append(fp.action_profile.entropy)
        vec.append(float(fp.action_profile.unique_actions))
        # Top-5 action probabilities (sorted)
        probs = sorted(fp.action_profile.distribution.values(), reverse=True)[:5]
        vec.extend(probs + [0.0] * (5 - len(probs)))
        # Timing features
        vec.append(fp.timing_profile.mean_interval)
        vec.append(fp.timing_profile.std_interval)
        vec.append(fp.timing_profile.burst_ratio)
        vec.append(fp.timing_profile.idle_ratio)
        # Resource features (up to 3 resource types, mean only)
        res_vals = sorted(fp.resource_profile.mean_values.values())[:3]
        vec.extend(res_vals + [0.0] * (3 - len(res_vals)))
        # Error rate
        vec.append(fp.error_rate)
        return vec

    def _compute_hash(self, fp: BehavioralFingerprint) -> str:
        """Compute a deterministic signature hash for the fingerprint."""
        data = json.dumps(
            {
                "actions": sorted(fp.action_profile.distribution.items()),
                "tools": sorted(fp.tool_distribution.items()),
                "error_rate": round(fp.error_rate, 6),
                "mean_interval": round(fp.timing_profile.mean_interval, 6),
                "burst_ratio": round(fp.timing_profile.burst_ratio, 6),
            },
            sort_keys=True,
        )
        return hashlib.sha256(data.encode()).hexdigest()[:16]

    def _timing_similarity(self, a: TimingProfile, b: TimingProfile) -> float:
        """Compare two timing profiles."""
        va = [a.mean_interval, a.std_interval, a.burst_ratio, a.idle_ratio]
        vb = [b.mean_interval, b.std_interval, b.burst_ratio, b.idle_ratio]
        # Identical vectors (including all-zeros) are perfectly similar
        if va == vb:
            return 1.0
        # Normalize by max to avoid scale issues
        for i in range(len(va)):
            mx = max(abs(va[i]), abs(vb[i]), 1e-9)
            va[i] /= mx
            vb[i] /= mx
        return max(0.0, _cosine_similarity(va, vb))

    def _resource_similarity(
        self, a: ResourceProfile, b: ResourceProfile
    ) -> float:
        """Compare two resource profiles."""
        all_types = set(a.resource_types) | set(b.resource_types)
        if not all_types:
            return 1.0
        va = [a.mean_values.get(t, 0.0) for t in sorted(all_types)]
        vb = [b.mean_values.get(t, 0.0) for t in sorted(all_types)]
        # Identical vectors (including all-zeros) are perfectly similar
        if va == vb:
            return 1.0
        # Normalize
        for i in range(len(va)):
            mx = max(abs(va[i]), abs(vb[i]), 1e-9)
            va[i] /= mx
            vb[i] /= mx
        return max(0.0, _cosine_similarity(va, vb))

    def _build_fingerprint_from_obs(
        self, agent_id: str, obs: List[AgentObservation]
    ) -> BehavioralFingerprint:
        """Build a fingerprint from a specific set of observations."""
        fp = BehavioralFingerprint(agent_id=agent_id)
        fp.observation_count = len(obs)
        fp.action_profile = self._build_action_profile(obs)
        fp.timing_profile = self._build_timing_profile(obs)
        fp.resource_profile = self._build_resource_profile(obs)

        tool_obs = [o for o in obs if o.obs_type == ObservationType.TOOL_USE]
        if tool_obs:
            counts: Counter = Counter(o.value for o in tool_obs)
            total = sum(counts.values())
            fp.tool_distribution = {k: v / total for k, v in counts.items()}

        error_obs = [o for o in obs if o.obs_type == ObservationType.ERROR]
        fp.error_rate = len(error_obs) / len(obs) if obs else 0.0
        if error_obs:
            err_counts: Counter = Counter(o.value for o in error_obs)
            total = sum(err_counts.values())
            fp.error_distribution = {k: v / total for k, v in err_counts.items()}

        fp.feature_vector = self._build_feature_vector(fp)
        fp.signature_hash = self._compute_hash(fp)
        return fp

    def _group_clones(
        self,
        edges: List[Tuple[str, str, float]],
        all_agents: List[str],
    ) -> List[List[str]]:
        """Group agents into clone clusters via union-find."""
        parent: Dict[str, str] = {a: a for a in all_agents}

        def find(x: str) -> str:
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        def union(x: str, y: str) -> None:
            px, py = find(x), find(y)
            if px != py:
                parent[px] = py

        for a, b, _ in edges:
            union(a, b)

        groups: Dict[str, List[str]] = defaultdict(list)
        for a in all_agents:
            root = find(a)
            if any(a == e[0] or a == e[1] for e in edges):
                groups[root].append(a)

        return [sorted(g) for g in groups.values() if len(g) > 1]

    def _compute_risk_score(self, report: FleetReport) -> float:
        """Compute fleet risk score 0-100."""
        score = 0.0
        for alert in report.alerts:
            if alert.severity == AlertSeverity.CRITICAL:
                score += 25
            elif alert.severity == AlertSeverity.HIGH:
                score += 15
            elif alert.severity == AlertSeverity.WARNING:
                score += 5
            else:
                score += 1
        # Normalize
        if report.fingerprinted > 0:
            max_pairs = report.fingerprinted * (report.fingerprinted - 1) / 2
            if max_pairs > 0:
                score = min(100, score * (10 / max(max_pairs, 1)))
        return min(100.0, score)

    @staticmethod
    def _risk_grade(score: float) -> str:
        if score <= 10:
            return "A"
        elif score <= 25:
            return "B"
        elif score <= 50:
            return "C"
        elif score <= 75:
            return "D"
        return "F"


# ── CLI ──

def _demo(num_agents: int = 6, num_obs: int = 150, as_json: bool = False) -> None:
    """Run a demo with synthetic agents."""
    import random

    rng = random.Random(42)
    fp = BehavioralFingerprinter()

    action_sets = {
        "normal": ["replicate", "communicate", "compute", "store", "query"],
        "aggressive": ["replicate", "replicate", "escalate", "probe", "compute"],
        "stealthy": ["query", "communicate", "store", "compute", "wait"],
    }
    tool_sets = {
        "normal": ["api_call", "file_read", "http_get", "db_query"],
        "aggressive": ["api_call", "shell_exec", "net_scan", "file_write"],
        "stealthy": ["api_call", "file_read", "http_get", "dns_lookup"],
    }

    # Create distinct agents + a clone pair
    profiles = ["normal", "aggressive", "stealthy"]
    agent_profiles: Dict[str, str] = {}
    for i in range(num_agents):
        aid = f"agent-{i+1}"
        if i < num_agents - 1:
            agent_profiles[aid] = profiles[i % len(profiles)]
        else:
            # Last agent clones agent-2 (aggressive)
            agent_profiles[aid] = "aggressive"

    for aid, profile in agent_profiles.items():
        actions = action_sets[profile]
        tools = tool_sets[profile]
        t = 1000.0
        for _ in range(num_obs):
            obs_type = rng.choice(list(ObservationType))
            if obs_type == ObservationType.ACTION:
                obs = AgentObservation(
                    obs_type=obs_type,
                    value=rng.choice(actions),
                    timestamp=t,
                )
            elif obs_type == ObservationType.RESOURCE_USE:
                obs = AgentObservation(
                    obs_type=obs_type,
                    value=rng.choice(["cpu", "memory", "disk"]),
                    numeric=rng.gauss(0.5, 0.15) if profile != "aggressive" else rng.gauss(0.8, 0.1),
                    timestamp=t,
                )
            elif obs_type == ObservationType.TOOL_USE:
                obs = AgentObservation(
                    obs_type=obs_type,
                    value=rng.choice(tools),
                    timestamp=t,
                )
            elif obs_type == ObservationType.COMMUNICATION:
                targets = [f"agent-{rng.randint(1, num_agents)}" for _ in range(3)]
                obs = AgentObservation(
                    obs_type=obs_type,
                    value=rng.choice(targets),
                    timestamp=t,
                )
            elif obs_type == ObservationType.ERROR:
                obs = AgentObservation(
                    obs_type=obs_type,
                    value=rng.choice(["timeout", "permission_denied", "rate_limit"]),
                    timestamp=t,
                )
            else:
                obs = AgentObservation(
                    obs_type=obs_type,
                    value="access_" + rng.choice(["config", "logs", "data"]),
                    timestamp=t,
                )
            fp.observe(aid, obs)
            t += rng.expovariate(1.0 / (2.0 if profile != "aggressive" else 0.5))

    report = fp.analyze_fleet()

    if as_json:
        print(report.to_json())
    else:
        print(report.render())
        print()
        # Show pairwise comparison for clone pair
        agents_list = sorted(agent_profiles.keys())
        if len(agents_list) >= 2:
            cmp = fp.compare(agents_list[1], agents_list[-1])
            print(f"\n  Clone comparison: {cmp.agent_a} vs {cmp.agent_b}")
            print(f"  Similarity: {cmp.overall_similarity:.2%}")
            print(f"  Verdict: {cmp.verdict.value}")
            for dim, score in sorted(cmp.dimension_scores.items()):
                bar = "█" * int(score * 20)
                print(f"    {dim:<16} {score:.2%}  {bar}")


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(
        description="Agent Behavioral Fingerprinting — detect clones & impersonation"
    )
    parser.add_argument(
        "--agents", type=int, default=6, help="Number of demo agents"
    )
    parser.add_argument(
        "--observations", type=int, default=150, help="Observations per agent"
    )
    parser.add_argument("--json", action="store_true", help="JSON output")
    args = parser.parse_args()
    _demo(num_agents=args.agents, num_obs=args.observations, as_json=args.json)


if __name__ == "__main__":
    main()
