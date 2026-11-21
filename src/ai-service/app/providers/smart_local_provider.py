"""Backward-compatible re-export of DeterministicFinancialEngine."""

from app.providers.deterministic_engine import (
    DeterministicFinancialEngine,
    SmartLocalProvider,
)

__all__ = [
    "DeterministicFinancialEngine",
    "SmartLocalProvider",
]
