import { Platform } from 'react-native';
import { generateUuidV4 } from '@/utils/uuid';
import { apiClient } from './client';
import type {
  AddInvestmentLotRequest,
  AiChatHistoryDto,
  AiChatResponseDto,
  AnalyticsSummaryDto,
  AuthResponse,
  BudgetDto,
  CategoryDto,
  CategoryType,
  ConfirmImportItemPayload,
  ConfirmImportResultDto,
  CreateRecurringTransactionRequest,
  ImportPreviewResultDto,
  InvestmentLotDto,
  InvestmentPortfolioDto,
  PaginatedList,
  ReceiptScanRequestDto,
  ReceiptScanResultDto,
  RecurringTransactionDto,
  RecurringTransactionsSummaryDto,
  SavingsGoalDto,
  TransactionDetailDto,
  TransactionDto,
  TransactionType,
  UpdateAssetPriceResultDto,
  UserProfileDto,
  WalletDto,
  WalletType,
  WorkspaceInvitationDto,
  WorkspaceMemberDto,
  WorkspaceRole,
  WorkspaceSummaryDto,
} from './types';

export interface LoginPayload {
  email: string;
  password: string;
}

export interface RegisterPayload {
  email: string;
  password: string;
  displayName: string;
  preferredLocale?: string;
}

export interface CreateWalletPayload {
  name: string;
  type: WalletType;
  initialBalance?: number;
  currencyCode?: string;
  creditLimit?: number | null;
  icon?: string | null;
  colorHex?: string | null;
}

export interface UpdateWalletPayload {
  name: string;
  creditLimit?: number | null;
  icon?: string | null;
  colorHex?: string | null;
  isArchived?: boolean;
  initialBalance?: number | null;
}

export interface CreateCategoryPayload {
  nameTr: string;
  nameEn?: string | null;
  type: CategoryType;
  icon: string;
  colorHex: string;
}

export interface TransactionQueryParams {
  dateFrom?: string;
  dateTo?: string;
  year?: number;
  month?: number;
  walletId?: string;
  categoryId?: string;
  type?: TransactionType;
  paidByUserId?: string;
  search?: string;
  pageNumber?: number;
  pageSize?: number;
}

export interface CreateTransactionPayload {
  type: TransactionType;
  walletId: string;
  counterpartyWalletId?: string | null;
  categoryId?: string | null;
  receiptId?: string | null;
  amount: number;
  currencyCode?: string;
  transactionDate: string;
  merchantName?: string | null;
  notes?: string | null;
  paidByUserId?: string | null;
  installmentCount?: number;
}

export interface UpdateTransactionPayload {
  type: TransactionType;
  walletId: string;
  counterpartyWalletId?: string | null;
  categoryId?: string | null;
  amount: number;
  transactionDate: string;
  merchantName?: string | null;
  notes?: string | null;
  paidByUserId?: string | null;
}

export interface UpsertBudgetPayload {
  categoryId?: string | null;
  year: number;
  month: number;
  limitAmount: number;
  alertThresholdPercent?: number;
}

export interface CreateSavingsGoalPayload {
  name: string;
  targetAmount: number;
  currentAmount?: number;
  linkedWalletId?: string | null;
  targetDate?: string | null;
}

export interface UpdateSavingsGoalPayload {
  name: string;
  targetAmount: number;
  targetDate?: string | null;
  linkedWalletId?: string | null;
  currentAmount?: number | null;
}

export interface CreateWorkspacePayload {
  name: string;
  defaultCurrency?: string;
}

export interface CreateInvitePayload {
  assignedRole?: Exclude<WorkspaceRole, 'Owner'>;
  invitedEmail?: string | null;
}

export const authApi = {
  async login(payload: LoginPayload): Promise<AuthResponse> {
    const { data } = await apiClient.post<AuthResponse>('/auth/login', payload);
    return data;
  },

  async register(payload: RegisterPayload): Promise<AuthResponse> {
    const { data } = await apiClient.post<AuthResponse>('/auth/register', payload);
    return data;
  },

  async logout(refreshToken?: string | null): Promise<void> {
    await apiClient.post('/auth/logout', refreshToken ? { refreshToken } : {});
  },
};

export const usersApi = {
  async getMe(): Promise<UserProfileDto> {
    const { data } = await apiClient.get<UserProfileDto>('/users/me');
    return data;
  },
};

// Workspaces & Household Collaboration
export async function listWorkspaces(): Promise<WorkspaceSummaryDto[]> {
  const { data } = await apiClient.get<WorkspaceSummaryDto[]>('/workspaces');
  return data;
}

export async function createWorkspace(
  payload: CreateWorkspacePayload,
): Promise<WorkspaceSummaryDto> {
  const { data } = await apiClient.post<WorkspaceSummaryDto>('/workspaces', {
    name: payload.name,
    defaultCurrency: payload.defaultCurrency ?? 'TRY',
  });
  return data;
}

export async function getWorkspaceMembers(
  workspaceId: string,
): Promise<WorkspaceMemberDto[]> {
  const { data } = await apiClient.get<WorkspaceMemberDto[]>(
    `/workspaces/${workspaceId}/members`,
  );
  return data;
}

export async function createInvite(
  workspaceId: string,
  payload: CreateInvitePayload = {},
): Promise<WorkspaceInvitationDto> {
  const { data } = await apiClient.post<WorkspaceInvitationDto>(
    `/workspaces/${workspaceId}/invitations`,
    {
      assignedRole: payload.assignedRole ?? 'Contributor',
      invitedEmail: payload.invitedEmail ?? null,
    },
  );
  return data;
}

export async function joinWorkspaceByCode(
  invitationCode: string,
): Promise<WorkspaceSummaryDto> {
  const { data } = await apiClient.post<WorkspaceSummaryDto>(
    '/workspaces/invitations/accept',
    { invitationCode: invitationCode.trim().toUpperCase() },
  );
  return data;
}

export const workspacesApi = {
  list: listWorkspaces,
  createWorkspace,
  getWorkspaceMembers,
  createInvite,
  joinWorkspaceByCode,
};

// Wallets
export async function getWallets(
  workspaceId: string,
  includeArchived = false,
): Promise<WalletDto[]> {
  const { data } = await apiClient.get<WalletDto[]>(
    `/workspaces/${workspaceId}/wallets`,
    { params: { includeArchived } },
  );
  return data;
}

export async function createWallet(
  workspaceId: string,
  payload: CreateWalletPayload,
): Promise<WalletDto> {
  const { data } = await apiClient.post<WalletDto>(
    `/workspaces/${workspaceId}/wallets`,
    {
      name: payload.name,
      type: payload.type,
      initialBalance: payload.initialBalance ?? 0,
      currencyCode: payload.currencyCode ?? 'TRY',
      creditLimit: payload.creditLimit ?? null,
      icon: payload.icon ?? null,
      colorHex: payload.colorHex ?? null,
    },
  );
  return data;
}

export async function updateWallet(
  workspaceId: string,
  walletId: string,
  payload: UpdateWalletPayload,
): Promise<WalletDto> {
  const { data } = await apiClient.put<WalletDto>(
    `/workspaces/${workspaceId}/wallets/${walletId}`,
    payload,
  );
  return data;
}

export async function archiveWallet(
  workspaceId: string,
  wallet: Pick<WalletDto, 'id' | 'name' | 'creditLimit' | 'icon' | 'colorHex'>,
): Promise<WalletDto> {
  return updateWallet(workspaceId, wallet.id, {
    name: wallet.name,
    creditLimit: wallet.creditLimit,
    icon: wallet.icon,
    colorHex: wallet.colorHex,
    isArchived: true,
  });
}

export async function deleteWallet(
  workspaceId: string,
  walletId: string,
): Promise<void> {
  await apiClient.delete(`/workspaces/${workspaceId}/wallets/${walletId}`);
}

export const walletsApi = {
  list: getWallets,
  createWallet,
  updateWallet,
  archiveWallet,
  deleteWallet,
};

// Categories
export async function getCategories(
  workspaceId: string,
  type?: CategoryType,
): Promise<CategoryDto[]> {
  const { data } = await apiClient.get<CategoryDto[]>(
    `/workspaces/${workspaceId}/categories`,
    { params: type ? { type } : undefined },
  );
  return data;
}

export async function createCategory(
  workspaceId: string,
  payload: CreateCategoryPayload,
): Promise<CategoryDto> {
  const { data } = await apiClient.post<CategoryDto>(
    `/workspaces/${workspaceId}/categories`,
    payload,
  );
  return data;
}

export const categoriesApi = {
  list: getCategories,
  getCategories,
  createCategory,
};

// Transactions
export async function getTransactions(
  workspaceId: string,
  params: TransactionQueryParams = {},
): Promise<PaginatedList<TransactionDto>> {
  const { data } = await apiClient.get<PaginatedList<TransactionDto>>(
    `/workspaces/${workspaceId}/transactions`,
    { params },
  );
  return data;
}

export async function createTransaction(
  workspaceId: string,
  payload: CreateTransactionPayload,
  idempotencyKey: string = generateUuidV4(),
): Promise<TransactionDetailDto> {
  const { data } = await apiClient.post<TransactionDetailDto>(
    `/workspaces/${workspaceId}/transactions`,
    {
      ...payload,
      currencyCode: payload.currencyCode ?? 'TRY',
      idempotencyKey,
    },
    {
      headers: {
        'Idempotency-Key': idempotencyKey,
      },
    },
  );
  return data;
}

export async function updateTransaction(
  workspaceId: string,
  transactionId: string,
  payload: UpdateTransactionPayload,
): Promise<TransactionDetailDto> {
  const { data } = await apiClient.put<TransactionDetailDto>(
    `/workspaces/${workspaceId}/transactions/${transactionId}`,
    payload,
  );
  return data;
}

export async function deleteTransaction(
  workspaceId: string,
  transactionId: string,
): Promise<void> {
  await apiClient.delete(`/workspaces/${workspaceId}/transactions/${transactionId}`);
}

export async function previewStatementImport(
  workspaceId: string,
  formData: FormData,
): Promise<ImportPreviewResultDto> {
  const { data } = await apiClient.post<ImportPreviewResultDto>(
    `/workspaces/${workspaceId}/transactions/import/preview`,
    formData,
    {
      timeout: 30_000,
      transformRequest: (body) => body,
    },
  );
  return data;
}

export async function confirmStatementImport(
  workspaceId: string,
  walletId: string,
  items: ConfirmImportItemPayload[],
): Promise<ConfirmImportResultDto> {
  const { data } = await apiClient.post<ConfirmImportResultDto>(
    `/workspaces/${workspaceId}/transactions/import/confirm`,
    { walletId, items },
  );
  return data;
}

export const transactionsApi = {
  list: getTransactions,
  getTransactions,
  createTransaction,
  updateTransaction,
  deleteTransaction,
  previewStatementImport,
  confirmStatementImport,
};

// Budgets
export async function getBudgets(
  workspaceId: string,
  year?: number,
  month?: number,
): Promise<BudgetDto[]> {
  const { data } = await apiClient.get<BudgetDto[]>(
    `/workspaces/${workspaceId}/budgets`,
    { params: { year, month } },
  );
  return data;
}

export async function upsertBudget(
  workspaceId: string,
  payload: UpsertBudgetPayload,
): Promise<BudgetDto> {
  const { data } = await apiClient.post<BudgetDto>(
    `/workspaces/${workspaceId}/budgets`,
    {
      categoryId: payload.categoryId ?? null,
      year: payload.year,
      month: payload.month,
      limitAmount: payload.limitAmount,
      alertThresholdPercent: payload.alertThresholdPercent ?? 80,
    },
  );
  return data;
}

export async function deleteBudget(
  workspaceId: string,
  budgetId: string,
): Promise<void> {
  await apiClient.delete(`/workspaces/${workspaceId}/budgets/${budgetId}`);
}

export const budgetsApi = {
  list: getBudgets,
  getBudgets,
  upsertBudget,
  deleteBudget,
};

// Savings Goals
export async function getSavingsGoals(
  workspaceId: string,
): Promise<SavingsGoalDto[]> {
  const { data } = await apiClient.get<SavingsGoalDto[]>(
    `/workspaces/${workspaceId}/savings-goals`,
  );
  return data;
}

export async function createSavingsGoal(
  workspaceId: string,
  payload: CreateSavingsGoalPayload,
): Promise<SavingsGoalDto> {
  const { data } = await apiClient.post<SavingsGoalDto>(
    `/workspaces/${workspaceId}/savings-goals`,
    {
      name: payload.name,
      targetAmount: payload.targetAmount,
      currentAmount: payload.currentAmount ?? 0,
      linkedWalletId: payload.linkedWalletId ?? null,
      targetDate: payload.targetDate ?? null,
    },
  );
  return data;
}

export async function updateSavingsGoal(
  workspaceId: string,
  goalId: string,
  payload: UpdateSavingsGoalPayload,
): Promise<SavingsGoalDto> {
  const { data } = await apiClient.put<SavingsGoalDto>(
    `/workspaces/${workspaceId}/savings-goals/${goalId}`,
    payload,
  );
  return data;
}

export async function deleteSavingsGoal(
  workspaceId: string,
  goalId: string,
): Promise<void> {
  await apiClient.delete(`/workspaces/${workspaceId}/savings-goals/${goalId}`);
}

export const savingsGoalsApi = {
  getSavingsGoals,
  createSavingsGoal,
  updateSavingsGoal,
  deleteSavingsGoal,
};

// Analytics
export async function getAnalyticsSummary(
  workspaceId: string,
  dateFrom?: string,
  dateTo?: string,
  year?: number,
  month?: number,
): Promise<AnalyticsSummaryDto> {
  const { data } = await apiClient.get<AnalyticsSummaryDto>(
    `/workspaces/${workspaceId}/analytics/summary`,
    { params: { dateFrom, dateTo, year, month } },
  );
  return data;
}

export const analyticsApi = {
  getSummary: getAnalyticsSummary,
  getAnalyticsSummary,
};

// AI Financial Assistant & Receipt OCR
export async function getChatHistory(
  workspaceId: string,
): Promise<AiChatHistoryDto> {
  const { data } = await apiClient.get<AiChatHistoryDto>(
    `/workspaces/${workspaceId}/ai/history`,
  );
  return data;
}

export async function clearChatHistory(workspaceId: string): Promise<void> {
  await apiClient.delete(`/workspaces/${workspaceId}/ai/history`);
}

export async function askAssistant(
  workspaceId: string,
  question: string,
): Promise<AiChatResponseDto> {
  const { data } = await apiClient.post<AiChatResponseDto>(
    `/workspaces/${workspaceId}/ai/chat`,
    { question },
    { timeout: 30_000 },
  );
  return data;
}

export async function scanReceipt(
  workspaceId: string,
  payload: ReceiptScanRequestDto,
): Promise<ReceiptScanResultDto> {
  const { data } = await apiClient.post<ReceiptScanResultDto>(
    `/workspaces/${workspaceId}/ai/scan-receipt`,
    payload,
    { timeout: 30_000 },
  );
  return data;
}

export async function scanReceiptFile(
  workspaceId: string,
  formData: FormData,
): Promise<ReceiptScanResultDto> {
  const { data } = await apiClient.post<ReceiptScanResultDto>(
    `/workspaces/${workspaceId}/ai/scan-receipt`,
    formData,
    {
      timeout: 30_000,
      headers: {
        // Let the browser generate the multipart boundary on Web; explicit on Native.
        'Content-Type':
          Platform.OS === 'web' ? undefined : 'multipart/form-data',
      },
    },
  );
  return data;
}

export const aiApi = {
  getChatHistory,
  clearChatHistory,
  askAssistant,
  scanReceipt,
  scanReceiptFile,
};

// Recurring Transactions
export async function getRecurringTransactions(
  workspaceId: string,
): Promise<RecurringTransactionsSummaryDto> {
  const { data } = await apiClient.get<RecurringTransactionsSummaryDto>(
    `/workspaces/${workspaceId}/recurring-transactions`,
  );
  return data;
}

export async function createRecurringTransaction(
  workspaceId: string,
  payload: CreateRecurringTransactionRequest,
): Promise<RecurringTransactionDto> {
  const { data } = await apiClient.post<RecurringTransactionDto>(
    `/workspaces/${workspaceId}/recurring-transactions`,
    payload,
  );
  return data;
}

export async function toggleRecurringTransaction(
  workspaceId: string,
  id: string,
): Promise<RecurringTransactionDto> {
  const { data } = await apiClient.patch<RecurringTransactionDto>(
    `/workspaces/${workspaceId}/recurring-transactions/${id}/toggle`,
  );
  return data;
}

export async function deleteRecurringTransaction(
  workspaceId: string,
  id: string,
): Promise<void> {
  await apiClient.delete(
    `/workspaces/${workspaceId}/recurring-transactions/${id}`,
  );
}

export const recurringApi = {
  list: getRecurringTransactions,
  create: createRecurringTransaction,
  toggle: toggleRecurringTransaction,
  remove: deleteRecurringTransaction,
};

// Investment Portfolio & Wealth Vault
export async function getInvestmentPortfolio(
  workspaceId: string,
): Promise<InvestmentPortfolioDto> {
  const { data } = await apiClient.get<InvestmentPortfolioDto>(
    `/workspaces/${workspaceId}/investments`,
  );
  return data;
}

export async function addInvestmentLot(
  workspaceId: string,
  payload: AddInvestmentLotRequest,
): Promise<InvestmentLotDto> {
  const { data } = await apiClient.post<InvestmentLotDto>(
    `/workspaces/${workspaceId}/investments`,
    payload,
  );
  return data;
}

export async function updateInvestmentPrice(
  workspaceId: string,
  symbol: string,
  currentUnitPrice: number,
): Promise<UpdateAssetPriceResultDto> {
  const { data } = await apiClient.patch<UpdateAssetPriceResultDto>(
    `/workspaces/${workspaceId}/investments/price`,
    { symbol, currentUnitPrice },
  );
  return data;
}

export async function deleteInvestmentLot(
  workspaceId: string,
  lotId: string,
): Promise<void> {
  await apiClient.delete(`/workspaces/${workspaceId}/investments/${lotId}`);
}

export const investmentsApi = {
  getPortfolio: getInvestmentPortfolio,
  addLot: addInvestmentLot,
  updatePrice: updateInvestmentPrice,
  deleteLot: deleteInvestmentLot,
};
