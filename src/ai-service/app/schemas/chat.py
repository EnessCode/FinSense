"""Pydantic v2 contracts for the Financial Coach Chat endpoint."""

from decimal import Decimal, InvalidOperation
from typing import Any, Optional, Union
from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

ChatIntent = str


class CategorySpendItem(BaseModel):
    """Deterministic category spending summary item computed by ASP.NET Core."""

    model_config = ConfigDict(extra="ignore")

    category_name: str = ""
    amount: Decimal = Decimal("0.00")
    percentage: Optional[Decimal] = None
    transaction_count: Optional[int] = None

    @field_validator("amount", mode="before")
    @classmethod
    def _coerce_amount(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")

    @field_validator("percentage", mode="before")
    @classmethod
    def _coerce_percentage(cls, v: Any) -> Optional[Decimal]:
        if v is None or v == "":
            return None
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return None


class BudgetAlertItem(BaseModel):
    """Deterministic budget utilization alert computed by ASP.NET Core."""

    model_config = ConfigDict(extra="ignore")

    category_name: str = ""
    limit_amount: Decimal = Decimal("0.00")
    spent_amount: Decimal = Decimal("0.00")
    utilization_percent: Decimal = Decimal("0.00")
    status: Optional[str] = None

    @field_validator("limit_amount", "spent_amount", "utilization_percent", mode="before")
    @classmethod
    def _coerce_budget_decimals(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")


class MemberSpendItem(BaseModel):
    """Deterministic household member spending breakdown item."""

    model_config = ConfigDict(extra="ignore")

    member_name: str = ""
    amount: Decimal = Decimal("0.00")
    percentage: Optional[Decimal] = None

    @field_validator("amount", mode="before")
    @classmethod
    def _coerce_amount(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")

    @field_validator("percentage", mode="before")
    @classmethod
    def _coerce_percentage(cls, v: Any) -> Optional[Decimal]:
        if v is None or v == "":
            return None
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return None


class RecurringObligationItemDto(BaseModel):
    """Deterministic recurring obligation item computed by ASP.NET Core."""

    model_config = ConfigDict(extra="ignore")

    title: str = ""
    type: str = "Expense"
    amount: Decimal = Decimal("0.00")
    day_of_month: int = 1
    total_occurrences: Optional[int] = None
    completed_occurrences: int = 0
    next_execution_date_utc: Optional[Any] = None

    @field_validator("amount", mode="before")
    @classmethod
    def _coerce_amount(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")


class InvestmentHoldingItemDto(BaseModel):
    """Deterministic investment holding item computed by ASP.NET Core."""

    model_config = ConfigDict(extra="ignore")

    symbol: str = ""
    asset_name: str = ""
    category: str = ""
    total_quantity: Decimal = Decimal("0.00")
    total_cost: Decimal = Decimal("0.00")
    current_value: Decimal = Decimal("0.00")
    profit_loss_amount: Decimal = Decimal("0.00")
    profit_loss_percent: Decimal = Decimal("0.00")

    @field_validator(
        "total_quantity",
        "total_cost",
        "current_value",
        "profit_loss_amount",
        "profit_loss_percent",
        mode="before",
    )
    @classmethod
    def _coerce_holding_decimals(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")


class InvestmentPortfolioDto(BaseModel):
    """Deterministic investment portfolio computed by ASP.NET Core."""

    model_config = ConfigDict(extra="ignore")

    total_invested_cost: Decimal = Decimal("0.00")
    total_current_value: Decimal = Decimal("0.00")
    total_profit_loss_amount: Decimal = Decimal("0.00")
    total_profit_loss_percent: Decimal = Decimal("0.00")
    top_holdings: list[InvestmentHoldingItemDto] = Field(default_factory=list)

    @field_validator(
        "total_invested_cost",
        "total_current_value",
        "total_profit_loss_amount",
        "total_profit_loss_percent",
        mode="before",
    )
    @classmethod
    def _coerce_portfolio_decimals(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")

    @field_validator("top_holdings", mode="before")
    @classmethod
    def _coerce_holdings_list(cls, v: Any) -> list:
        if v is None:
            return []
        return v


class RecurringSummaryDto(BaseModel):
    """Deterministic recurring payments summary item."""

    model_config = ConfigDict(extra="ignore")

    total_monthly_amount: Decimal = Decimal("0.00")
    active_count: int = 0


class InvestmentsSummaryDto(BaseModel):
    """Deterministic portfolio summary item."""

    model_config = ConfigDict(extra="ignore")

    total_value: Decimal = Decimal("0.00")
    total_cost: Decimal = Decimal("0.00")
    unrealized_profit_loss: Decimal = Decimal("0.00")
    profit_loss_percentage: Optional[Decimal] = None
    asset_counts: Optional[dict[str, int]] = None
    asset_count: Optional[int] = None


class FinancialContextDto(BaseModel):
    """Pre-calculated deterministic financial summary sent by ASP.NET Core."""

    model_config = ConfigDict(extra="ignore")

    total_balance: Decimal = Decimal("0.00")
    monthly_income: Decimal = Decimal("0.00")
    monthly_expense: Decimal = Decimal("0.00")
    net_cash_flow: Decimal = Decimal("0.00")
    savings_rate_percent: Decimal = Decimal("0.00")
    top_categories: list[CategorySpendItem] = Field(default_factory=list)
    budget_alerts: list[BudgetAlertItem] = Field(default_factory=list)
    member_breakdown: list[MemberSpendItem] = Field(default_factory=list)

    # Extended fields from ASP.NET Core
    monthly_fixed_income: Decimal = Decimal("0.00")
    monthly_fixed_expense: Decimal = Decimal("0.00")
    recurring_obligations: list[RecurringObligationItemDto] = Field(default_factory=list)
    investment_portfolio: Optional[InvestmentPortfolioDto] = None

    # Legacy summaries
    recurring_summary: Optional[RecurringSummaryDto] = None
    investments_summary: Optional[InvestmentsSummaryDto] = None

    @field_validator(
        "total_balance",
        "monthly_income",
        "monthly_expense",
        "net_cash_flow",
        "savings_rate_percent",
        "monthly_fixed_income",
        "monthly_fixed_expense",
        mode="before",
    )
    @classmethod
    def _coerce_decimal(cls, v: Any) -> Decimal:
        if v is None or v == "":
            return Decimal("0.00")
        try:
            return Decimal(str(v))
        except (InvalidOperation, ValueError, TypeError):
            return Decimal("0.00")

    @field_validator(
        "top_categories",
        "budget_alerts",
        "member_breakdown",
        "recurring_obligations",
        mode="before",
    )
    @classmethod
    def _coerce_lists(cls, v: Any) -> list:
        if v is None:
            return []
        return v

    @model_validator(mode="after")
    def sync_summaries(self) -> "FinancialContextDto":
        if self.investment_portfolio is not None and self.investments_summary is None:
            self.investments_summary = InvestmentsSummaryDto(
                total_value=self.investment_portfolio.total_current_value,
                total_cost=self.investment_portfolio.total_invested_cost,
                unrealized_profit_loss=self.investment_portfolio.total_profit_loss_amount,
                profit_loss_percentage=self.investment_portfolio.total_profit_loss_percent,
                asset_count=len(self.investment_portfolio.top_holdings),
            )
        if self.recurring_obligations and self.recurring_summary is None:
            self.recurring_summary = RecurringSummaryDto(
                total_monthly_amount=sum((r.amount for r in self.recurring_obligations), Decimal("0.00")),
                active_count=len(self.recurring_obligations),
            )
        return self


class ChatRequestDto(BaseModel):
    """Request payload for the internal financial coaching endpoint."""

    model_config = ConfigDict(extra="ignore")

    question: str = ""
    locale: str = Field(default="tr-TR", max_length=32)
    workspace_type: str = Field(default="Personal", max_length=64)
    financial_context: FinancialContextDto = Field(default_factory=FinancialContextDto)

    @field_validator("financial_context", mode="before")
    @classmethod
    def _coerce_financial_context(cls, v: Any) -> Any:
        if v is None:
            return {}
        return v

    @field_validator("question", mode="before")
    @classmethod
    def _coerce_question(cls, v: Any) -> str:
        if v is None:
            return ""
        return str(v)

    @field_validator("locale", mode="before")
    @classmethod
    def _coerce_locale(cls, v: Any) -> str:
        if not v:
            return "tr-TR"
        return str(v)

    @field_validator("workspace_type", mode="before")
    @classmethod
    def _coerce_workspace_type(cls, v: Any) -> str:
        if not v:
            return "Personal"
        return str(v)


class ChatResponseDto(BaseModel):
    """Structured financial coach response enforced via schema and deterministic engine."""

    model_config = ConfigDict(extra="ignore")

    is_finance_related: bool = True
    intent: str = "SPENDING_QUERY"
    answer: str = ""
    highlighted_category: Optional[str] = None
    suggested_action: Optional[str] = None
    provider_used: str = "deterministic-engine"
