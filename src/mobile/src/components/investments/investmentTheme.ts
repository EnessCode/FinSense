import type { InvestmentAssetCategory } from '@/api/types';

export interface AssetPreset {
  symbol: string;
  assetName: string;
}

export const INVESTMENT_CATEGORIES: InvestmentAssetCategory[] = [
  'Gold',
  'Bist',
  'Crypto',
  'Forex',
  'Fund',
];

export const CATEGORY_COLORS: Record<InvestmentAssetCategory, string> = {
  Gold: '#D97706',
  Bist: '#0F766E',
  Crypto: '#334155',
  Forex: '#15803D',
  Fund: '#92400E',
};

export const CATEGORY_PRESETS: Record<InvestmentAssetCategory, AssetPreset[]> = {
  Gold: [
    { symbol: 'GAU', assetName: 'Gram Altın' },
    { symbol: 'CEYREK', assetName: 'Çeyrek Altın' },
    { symbol: 'TAM', assetName: 'Tam Altın' },
    { symbol: 'XAG', assetName: 'Gümüş (Gr)' },
  ],
  Bist: [
    { symbol: 'THYAO', assetName: 'Türk Hava Yolları' },
    { symbol: 'ASELS', assetName: 'Aselsan' },
    { symbol: 'TUPRS', assetName: 'Tüpraş' },
    { symbol: 'GARAN', assetName: 'Garanti BBVA' },
    { symbol: 'SISE', assetName: 'Şişecam' },
  ],
  Crypto: [
    { symbol: 'BTC', assetName: 'Bitcoin' },
    { symbol: 'ETH', assetName: 'Ethereum' },
    { symbol: 'SOL', assetName: 'Solana' },
    { symbol: 'AVAX', assetName: 'Avalanche' },
  ],
  Forex: [
    { symbol: 'USD', assetName: 'Amerikan Doları' },
    { symbol: 'EUR', assetName: 'Euro' },
    { symbol: 'GBP', assetName: 'İngiliz Sterlini' },
  ],
  Fund: [
    { symbol: 'AFT', assetName: 'Ak Portföy Yeni Teknolojiler' },
    { symbol: 'MAC', assetName: 'Marmara Capital Hisse Fonu' },
    { symbol: 'TI2', assetName: 'İş Portföy BIST 30 Fonu' },
  ],
};
