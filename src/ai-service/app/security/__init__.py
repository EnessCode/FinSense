"""Internal security package for FinSense microservice."""

from app.security.auth import verify_internal_api_key

__all__ = ["verify_internal_api_key"]
