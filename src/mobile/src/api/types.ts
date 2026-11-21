export type WorkspaceType = 'Personal' | 'Household';
export type WorkspaceRole = 'Owner' | 'Admin' | 'Contributor' | 'Viewer';
export type WalletType = 'Cash' | 'BankAccount' | 'CreditCard' | 'Savings';
export type CategoryType = 'Income' | 'Expense';
export type TransactionType = 'Income' | 'Expense' | 'Transfer';
export type BudgetStatus = 'Normal' | 'Warning' | 'Exceeded';
export type ReceiptStatus = 'PendingConfirmation' | 'LinkedToTransaction' | 'MarkedForDeletion';

export interface AuthTokensDto {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export interface AuthUserDto {
  id: string;
  email: string;
  displayName: string;
  preferredLocale: string;
  personalWorkspaceId: string;
}

export interface AuthResponse {
  user: AuthUserDto;
  tokens: AuthTokensDto;
}

export interface UserWorkspaceMembershipDto {
  workspaceId: string;
  name: string;
  type: WorkspaceType;
  defaultCurrency: string;
  role: WorkspaceRole;
  joinedAtUtc: string;
}

export interface UserProfileDto {
  id: string;
  email: string;
  displayName: string;
  preferredLocale: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  workspaces: UserWorkspaceMembershipDto[];
}

export interface WorkspaceSummaryDto {
  id: string;
  name: string;
  type: WorkspaceType;
  defaultCurrency: string;
  currentUserRole: WorkspaceRole;
  createdAtUtc: string;
  joinedAtUtc: string;
}

export interface WorkspaceMemberDto {
  userId: string;
  displayName: string;
  email: string;
  role: WorkspaceRole;
  joinedAtUtc: string;
}

export interface WorkspaceInvitationDto {
  id: string;
  workspaceId: string;
  invitationCode: string;
  invitedEmail: string | null;
  assignedRole: WorkspaceRole;
  expiresAtUtc: string;
}

export interface WalletDto {
  id: string;
  workspaceId: string;
  name: string;
  type: WalletType;
  currencyCode: string;
  initialBalance: number;
  currentBalance: number;
  creditLimit: number | null;
  availableCredit: number | null;
  icon: string | null;
  colorHex: string | null;
  isArchived: boolean;
}

export interface CategoryDto {
  id: string;
  name: string;
  nameTr: string;
  nameEn: string | null;
  type: CategoryType;
  icon: string;
  colorHex: string;
  isSystem: boolean;
}

export interface TransactionDto {
  id: string;
  workspaceId: string;
  walletId: string;
  walletName: string;
  counterpartyWalletId: string | null;
  counterpartyWalletName: string | null;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColorHex: string | null;
  receiptId: string | null;
  type: TransactionType;
  amount: number;
  currencyCode: string;
  transactionDate: string;
  merchantName: string | null;
  notes: string | null;
  createdByUserId: string | null;
  createdByDisplayName: string | null;
  paidByUserId: string | null;
  paidByDisplayName: string | null;
  createdAtUtc: string;
}

export interface TransactionDetailDto {
  id: string;
  workspaceId: string;
  walletId: string;
  walletName: string;
  counterpartyWalletId: string | null;
  counterpartyWalletName: string | null;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColorHex: string | null;
  type: TransactionType;
  amount: number;
  currencyCode: string;
  originalAmount: number;
  originalCurrencyCode: string;
  exchangeRate: number;
  transactionDate: string;
  merchantName: string | null;
  notes: string | null;
  createdByUserId: string | null;
  createdByDisplayName: string | null;
  paidByUserId: string | null;
  paidByDisplayName: string | null;
  idempotencyKey: string | null;
  createdAtUtc: string;
}

export interface PaginatedList<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface SavingsGoalDto {
  id: string;
  workspaceId: string;
  linkedWalletId: string | null;
  linkedWalletName: string | null;
  name: string;
  targetAmount: number;
  currentAmount: number;
  remainingAmount: number;
  completionPercent: number;
  currencyCode: string;
  targetDate: string | null;
}

export interface BudgetDto {
  id: string;
  workspaceId: string;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColorHex: string | null;
  year: number;
  month: number;
  limitAmount: number;
  spentAmount: number;
  remainingAmount: number;
  utilizationPercent: number;
  alertThresholdPercent: number;
  status: BudgetStatus;
  currencyCode: string;
}

export interface CategoryBreakdownItemDto {
  categoryId: string;
  categoryName: string;
  icon: string;
  colorHex: string;
  amount: number;
  percentage: number;
  transactionCount: number;
}

export interface MemberBreakdownItemDto {
  userId: string | null;
  displayName: string;
  incomeAmount: number;
  expenseAmount: number;
  transactionCount: number;
}

export interface AnalyticsSummaryDto {
  dateFrom: string;
  dateTo: string;
  currencyCode: string;
  totalIncome: number;
  totalExpense: number;
  netCashFlow: number;
  savingsRatePercent: number;
  categoryBreakdown: CategoryBreakdownItemDto[];
  memberBreakdown: MemberBreakdownItemDto[];
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export interface AiChatResponseDto {
  isFinanceRelated: boolean;
  intent: string;
  answer: string;
  highlightedCategory?: string | null;
  suggestedAction?: string | null;
  providerUsed: string;
  remainingDailyQuota: number;
}

export interface AiChatMessageDto {
  id: string;
  role: 'User' | 'Assistant' | 'user' | 'assistant';
  content: string;
  isFinanceRelated?: boolean;
  intent?: string | null;
  highlightedCategory?: string | null;
  suggestedAction?: string | null;
  providerUsed?: string | null;
  createdAtUtc: string;
}

export interface AiChatHistoryDto {
  conversationId?: string | null;
  messages: AiChatMessageDto[];
  remainingDailyQuota: number;
}

export interface ReceiptScanRequestDto {
  imageBase64?: string | null;
  rawOcrText?: string | null;
  fileName?: string | null;
  mimeType?: string | null;
}

export interface ReceiptScanResultDto {
  isValidReceipt: boolean;
  rejectionReason?: string | null;
  merchantName?: string | null;
  transactionDate?: string | null;
  totalAmount?: number | null;
  taxAmount?: number | null;
  currency: string;
  suggestedCategoryName?: string | null;
  suggestedCategoryId?: string | null;
  confidence: number;
  providerUsed: string;
  remainingDailyQuota: number;
}

export type RecurringTransactionType = 'Income' | 'Expense';

export interface RecurringTransactionDto {
  id: string;
  walletId: string;
  walletName: string;
  categoryId: string;
  categoryName: string;
  type: RecurringTransactionType;
  amount: number;
  currency: string;
  title: string;
  dayOfMonth: number;
  totalOccurrences?: number | null;
  completedOccurrences: number;
  remainingOccurrences?: number | null;
  nextExecutionDateUtc: string;
  lastExecutedAtUtc?: string | null;
  isActive: boolean;
}

export interface RecurringTransactionsSummaryDto {
  items: RecurringTransactionDto[];
  totalMonthlyFixedIncome: number;
  totalMonthlyFixedExpense: number;
  netMonthlyFixedFlow: number;
}

export interface CreateRecurringTransactionRequest {
  walletId: string;
  categoryId: string;
  type: RecurringTransactionType;
  amount: number;
  title: string;
  dayOfMonth: number;
  totalOccurrences?: number | null;
  executeFirstImmediately: boolean;
}

export type InvestmentAssetCategory =
  | 'Gold'
  | 'Bist'
  | 'Crypto'
  | 'Forex'
  | 'Fund';

export interface InvestmentLotDto {
  id: string;
  quantity: number;
  unitPrice: number;
  totalCost: number;
  purchaseDateUtc: string;
  notes?: string | null;
}

export interface InvestmentHoldingDto {
  symbol: string;
  assetName: string;
  category: InvestmentAssetCategory;
  totalQuantity: number;
  averageUnitCost: number;
  totalCost: number;
  currentUnitPrice: number;
  currentValue: number;
  profitLossAmount: number;
  profitLossPercent: number;
  currency: string;
  lots: InvestmentLotDto[];
}

export interface InvestmentCategoryAllocationDto {
  category: InvestmentAssetCategory;
  totalCost: number;
  currentValue: number;
  sharePercent: number;
}

export interface InvestmentPortfolioDto {
  totalInvestedCost: number;
  totalCurrentValue: number;
  totalProfitLossAmount: number;
  totalProfitLossPercent: number;
  categoryAllocation: InvestmentCategoryAllocationDto[];
  holdings: InvestmentHoldingDto[];
}

export interface UpdateAssetPriceResultDto {
  symbol: string;
  currentUnitPrice: number;
  updatedLotCount: number;
}

export interface AddInvestmentLotRequest {
  category: InvestmentAssetCategory;
  symbol: string;
  assetName: string;
  quantity: number;
  unitPrice: number;
  totalCost?: number | null;
  currentUnitPrice?: number | null;
  purchaseDateUtc?: string | null;
  notes?: string | null;
}

export interface ImportPreviewItemDto {
  rowIndex: number;
  transactionDate: string;
  description: string;
  type: 'Income' | 'Expense';
  amount: number;
  categoryId?: string | null;
  categoryName?: string | null;
  categoryIcon?: string | null;
  categoryColorHex?: string | null;
  isDuplicate: boolean;
}

export interface ImportPreviewResultDto {
  walletId: string;
  walletName: string;
  currencyCode: string;
  fileName: string;
  detectedFormat: string;
  totalRows: number;
  duplicateCount: number;
  totalIncome: number;
  totalExpense: number;
  items: ImportPreviewItemDto[];
}

export interface ConfirmImportItemPayload {
  transactionDate: string;
  description: string;
  type: 'Income' | 'Expense';
  amount: number;
  categoryId: string;
}

export interface ConfirmImportResultDto {
  walletId: string;
  importedCount: number;
  totalIncome: number;
  totalExpense: number;
  updatedWalletBalance: number;
}
