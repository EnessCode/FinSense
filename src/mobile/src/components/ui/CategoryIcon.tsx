import {
  ArrowLeftRight,
  Car,
  FileText,
  HeartPulse,
  Home,
  Laptop,
  PlusCircle,
  Repeat,
  ShoppingBag,
  ShoppingCart,
  TrendingUp,
  Utensils,
  Wallet,
} from 'lucide-react-native';
import React from 'react';
import type { TransactionType } from '@/api/types';
import { useTheme } from '@/context/ThemeContext';

export interface CategoryIconProps {
  iconName?: string | null;
  transactionType?: TransactionType;
  size?: number;
  color?: string;
}

export function CategoryIcon({
  iconName,
  transactionType,
  size = 18,
  color,
}: CategoryIconProps) {
  const { colors } = useTheme();
  const resolvedColor = color ?? colors.ink;

  if (transactionType === 'Transfer') {
    return <ArrowLeftRight size={size} color={resolvedColor} strokeWidth={1.75} />;
  }

  switch (iconName) {
    case 'shopping-cart':
      return <ShoppingCart size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'home':
      return <Home size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'file-text':
      return <FileText size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'car':
      return <Car size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'utensils':
      return <Utensils size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'heart-pulse':
      return <HeartPulse size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'shopping-bag':
      return <ShoppingBag size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'repeat':
      return <Repeat size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'wallet':
      return <Wallet size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'laptop':
      return <Laptop size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'trending-up':
      return <TrendingUp size={size} color={resolvedColor} strokeWidth={1.75} />;
    case 'plus-circle':
      return <PlusCircle size={size} color={resolvedColor} strokeWidth={1.75} />;
    default:
      return <Wallet size={size} color={resolvedColor} strokeWidth={1.75} />;
  }
}
