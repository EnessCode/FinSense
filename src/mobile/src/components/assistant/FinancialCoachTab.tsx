import * as Haptics from 'expo-haptics';
import {
  AlertCircle,
  ArrowUp,
  Lightbulb,
  RotateCcw,
  ShieldAlert,
  Sparkles,
  Tag,
} from 'lucide-react-native';
import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  Keyboard,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { toApiError } from '@/api/client';
import { aiApi } from '@/api/endpoints';
import type { AiChatMessageDto, AiChatResponseDto } from '@/api/types';
import { SurfaceCard } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { generateUuidV4 } from '@/utils/uuid';

const MAX_QUESTION_LENGTH = 250;

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  response?: Partial<AiChatResponseDto>;
}

function mapHistoryMessage(dto: AiChatMessageDto): ChatMessage {
  const isUser = dto.role.toLowerCase() === 'user';
  return {
    id: dto.id,
    role: isUser ? 'user' : 'assistant',
    text: dto.content,
    response: isUser
      ? undefined
      : {
          isFinanceRelated: dto.isFinanceRelated ?? true,
          intent: dto.intent ?? '',
          highlightedCategory: dto.highlightedCategory ?? null,
          suggestedAction: dto.suggestedAction ?? null,
          providerUsed: dto.providerUsed ?? undefined,
        },
  };
}

export interface FinancialCoachTabProps {
  workspaceId?: string;
  isHousehold: boolean;
  onQuotaUpdate: (remaining: number) => void;
}

export function FinancialCoachTab({
  workspaceId,
  isHousehold,
  onQuotaUpdate,
}: FinancialCoachTabProps) {
  const { t } = useTranslation();
  const { activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const scrollRef = useRef<any>(null);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [questionInput, setQuestionInput] = useState('');
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [isClearing, setIsClearing] = useState(false);
  const [isSending, setIsSending] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const scrollToEnd = () => {
    setTimeout(() => {
      scrollRef.current?.scrollToEnd({ animated: true });
    }, 80);
  };

  // Hydrate persistent workspace-scoped chat history on mount or workspace switch
  useEffect(() => {
    setMessages([]);
    setQuestionInput('');
    setErrorMessage(null);

    if (!workspaceId) {
      setIsLoadingHistory(false);
      return;
    }

    let isMounted = true;
    async function loadHistory() {
      setIsLoadingHistory(true);
      try {
        const history = await aiApi.getChatHistory(workspaceId!);
        if (!isMounted) return;

        if (typeof history.remainingDailyQuota === 'number') {
          onQuotaUpdate(history.remainingDailyQuota);
        }

        const mapped = (history.messages ?? []).map(mapHistoryMessage);
        setMessages(mapped);
        if (mapped.length > 0) {
          scrollToEnd();
        }
      } catch {
        // Keep empty state gracefully if history fetch fails
      } finally {
        if (isMounted) {
          setIsLoadingHistory(false);
        }
      }
    }

    void loadHistory();
    return () => {
      isMounted = false;
    };
  }, [workspaceId, onQuotaUpdate]);

  const quickPrompts = useMemo(() => {
    const base = [
      t('assistant.coach.prompts.summary'),
      t('assistant.coach.prompts.budgetRisk'),
      t('assistant.coach.prompts.savings'),
    ];
    if (isHousehold) {
      base.push(t('assistant.coach.prompts.householdShare'));
    }
    return base;
  }, [isHousehold, t]);

  const handleClearHistory = async () => {
    if (!workspaceId || isClearing || isSending) return;
    setErrorMessage(null);
    setIsClearing(true);
    try {
      await aiApi.clearChatHistory(workspaceId);
      setMessages([]);
      if (Platform.OS !== 'web') {
        void Haptics.selectionAsync();
      }
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsClearing(false);
    }
  };

  const handleSend = async (overrideText?: string) => {
    if (!workspaceId || isSending) return;

    const trimmed = (overrideText ?? questionInput).trim().slice(0, MAX_QUESTION_LENGTH);
    if (!trimmed) return;

    if (Platform.OS !== 'web') {
      void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    }

    setErrorMessage(null);
    if (!overrideText) {
      setQuestionInput('');
    }

    const userMsg: ChatMessage = {
      id: generateUuidV4(),
      role: 'user',
      text: trimmed,
    };

    setMessages((prev) => [...prev, userMsg]);
    scrollToEnd();
    setIsSending(true);

    try {
      const reply = await aiApi.askAssistant(workspaceId, trimmed);
      onQuotaUpdate(reply.remainingDailyQuota);

      const assistantMsg: ChatMessage = {
        id: generateUuidV4(),
        role: 'assistant',
        text: reply.answer,
        response: reply,
      };
      setMessages((prev) => [...prev, assistantMsg]);
      scrollToEnd();
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsSending(false);
    }
  };

  const canSend = Boolean(workspaceId) && !isSending && questionInput.trim().length > 0;
  const [keyboardHeight, setKeyboardHeight] = useState(0);

  useEffect(() => {
    if (Platform.OS !== 'android') {
      return;
    }

    const showSub = Keyboard.addListener('keyboardDidShow', (event) => {
      setKeyboardHeight(event.endCoordinates?.height ?? 0);
      setTimeout(() => {
        scrollRef.current?.scrollToEnd({ animated: true });
      }, 100);
    });
    const hideSub = Keyboard.addListener('keyboardDidHide', () => {
      setKeyboardHeight(0);
    });

    return () => {
      showSub.remove();
      hideSub.remove();
    };
  }, []);

  return (
    <KeyboardAvoidingView
      style={styles.flex}
      enabled={Platform.OS === 'ios'}
      behavior="padding"
      keyboardVerticalOffset={110}
    >
      <View
        style={[
          styles.flex,
          { paddingBottom: Platform.OS === 'android' ? keyboardHeight : 0 },
        ]}
      >
      {/* Subtle Sub-Header Action Bar when conversation exists */}
      {messages.length > 0 ? (
        <View style={styles.historyActionBar}>
          <Text
            style={[
              styles.historyTitle,
              isDark && { color: themeColors.inkSecondary },
            ]}
          >
            {t('assistant.coach.historyLabel')}
          </Text>
          <Pressable
            onPress={() => void handleClearHistory()}
            disabled={isClearing || isSending}
            hitSlop={8}
            style={({ pressed }) => [
              styles.clearPill,
              {
                borderColor: isDark ? 'rgba(148, 163, 184, 0.22)' : colors.border,
                backgroundColor: isDark
                  ? pressed
                    ? '#182234'
                    : '#131B29'
                  : pressed
                    ? colors.surfaceSubtle
                    : colors.surface,
                opacity: isClearing ? 0.6 : 1,
              },
            ]}
          >
            {isClearing ? (
              <ActivityIndicator
                size="small"
                color={isDark ? '#94A3B8' : colors.inkSecondary}
              />
            ) : (
              <RotateCcw
                size={13}
                color={isDark ? '#94A3B8' : colors.inkSecondary}
                strokeWidth={1.75}
              />
            )}
            <Text
              style={[
                styles.clearPillText,
                isDark && { color: '#CBD5E1' },
              ]}
            >
              {t('assistant.coach.clearChatButton')}
            </Text>
          </Pressable>
        </View>
      ) : null}

      <ScrollView
        ref={scrollRef}
        style={styles.flex}
        showsVerticalScrollIndicator={false}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={[styles.feedContent, { paddingBottom: 16 }]}
      >
        {isLoadingHistory ? (
          <View style={styles.historyLoadingWrap}>
            <ActivityIndicator
              size="small"
              color={isDark ? '#34D399' : activeTheme.primarySurface}
            />
          </View>
        ) : messages.length === 0 ? (
          <SurfaceCard padding="xl" style={styles.welcomeCard}>
            <View
              style={[
                styles.welcomeIcon,
                {
                  backgroundColor: isDark
                    ? 'rgba(16, 185, 129, 0.14)'
                    : activeTheme.softTint,
                  borderColor: isDark
                    ? 'rgba(52, 211, 153, 0.28)'
                    : activeTheme.softTintBorder,
                },
              ]}
            >
              <Sparkles
                size={20}
                color={isDark ? '#34D399' : activeTheme.primarySurface}
                strokeWidth={1.75}
              />
            </View>
            <Text
              style={[
                styles.welcomeTitle,
                isDark && { color: '#F8FAFC' },
              ]}
            >
              {t('assistant.coach.welcomeTitle')}
            </Text>
            <Text
              style={[
                styles.welcomeBody,
                isDark && { color: '#CBD5E1' },
              ]}
            >
              {t('assistant.coach.welcomeBody')}
            </Text>
          </SurfaceCard>
        ) : null}

        {/* Quick Prompt Chips */}
        <View style={styles.promptsSection}>
          <Text
            style={[
              styles.promptsLabel,
              isDark && { color: '#94A3B8' },
            ]}
          >
            {t('assistant.coach.promptsLabel')}
          </Text>
          <View style={styles.promptsWrap}>
            {quickPrompts.map((prompt) => (
              <Pressable
                key={prompt}
                disabled={isSending}
                onPress={() => void handleSend(prompt)}
                style={({ pressed }) => [
                  styles.promptChip,
                  {
                    borderColor: isDark
                      ? 'rgba(148, 163, 184, 0.22)'
                      : activeTheme.softTintBorder,
                    backgroundColor: isDark
                      ? pressed
                        ? '#182234'
                        : '#131B29'
                      : pressed
                        ? activeTheme.softTint
                        : colors.surface,
                  },
                ]}
              >
                <Text
                  style={[
                    styles.promptChipText,
                    { color: isDark ? '#CBD5E1' : activeTheme.primarySurface },
                  ]}
                >
                  {prompt}
                </Text>
              </Pressable>
            ))}
          </View>
        </View>

        {/* Conversation Feed */}
        <View style={styles.messagesList}>
          {messages.map((msg) => {
            if (msg.role === 'user') {
              return (
                <View key={msg.id} style={styles.userRow}>
                  <View
                    style={[
                      styles.userBubble,
                      {
                        backgroundColor: isDark
                          ? '#10B981'
                          : activeTheme.primarySurface,
                      },
                    ]}
                  >
                    <Text style={styles.userBubbleText}>{msg.text}</Text>
                  </View>
                </View>
              );
            }

            const resp = msg.response;
            const isOutOfScope = resp?.isFinanceRelated === false;

            return (
              <View key={msg.id} style={styles.assistantRow}>
                <SurfaceCard padding="lg" style={styles.assistantCard}>
                  {isOutOfScope ? (
                    <View
                      style={[
                        styles.guardrailBadge,
                        isDark && {
                          backgroundColor: themeColors.warningSoft,
                          borderColor: 'rgba(251, 191, 36, 0.28)',
                        },
                      ]}
                    >
                      <ShieldAlert
                        size={14}
                        color={isDark ? themeColors.warning : colors.warning}
                        strokeWidth={1.75}
                      />
                      <Text
                        style={[
                          styles.guardrailText,
                          isDark && { color: themeColors.warning },
                        ]}
                      >
                        {t('assistant.coach.guardrailNotice')}
                      </Text>
                    </View>
                  ) : null}

                  <Text
                    style={[
                      styles.assistantAnswer,
                      isDark && { color: '#F8FAFC' },
                    ]}
                  >
                    {msg.text}
                  </Text>

                  {resp?.highlightedCategory ? (
                    <View
                      style={[
                        styles.categoryPill,
                        {
                          backgroundColor: isDark
                            ? 'rgba(16, 185, 129, 0.14)'
                            : activeTheme.softTint,
                          borderColor: isDark
                            ? 'rgba(52, 211, 153, 0.28)'
                            : activeTheme.softTintBorder,
                        },
                      ]}
                    >
                      <Tag
                        size={13}
                        color={isDark ? '#34D399' : activeTheme.primarySurface}
                        strokeWidth={1.75}
                      />
                      <Text
                        style={[
                          styles.categoryPillText,
                          {
                            color: isDark
                              ? '#34D399'
                              : activeTheme.primarySurface,
                          },
                        ]}
                      >
                        {t('assistant.coach.highlightedCategoryLabel', {
                          name: resp.highlightedCategory,
                        })}
                      </Text>
                    </View>
                  ) : null}

                  {resp?.suggestedAction ? (
                    <View
                      style={[
                        styles.actionBox,
                        {
                          backgroundColor: isDark
                            ? '#182234'
                            : activeTheme.softTint,
                          borderColor: isDark
                            ? 'rgba(148, 163, 184, 0.14)'
                            : activeTheme.softTintBorder,
                        },
                      ]}
                    >
                      <View style={styles.actionBoxHeader}>
                        <Lightbulb
                          size={14}
                          color={isDark ? '#34D399' : activeTheme.primarySurface}
                          strokeWidth={1.75}
                        />
                        <Text
                          style={[
                            styles.actionBoxTitle,
                            {
                              color: isDark
                                ? '#34D399'
                                : activeTheme.primarySurface,
                            },
                          ]}
                        >
                          {t('assistant.coach.suggestedActionTitle')}
                        </Text>
                      </View>
                      <Text
                        style={[
                          styles.actionBoxBody,
                          isDark && { color: '#CBD5E1' },
                        ]}
                      >
                        {resp.suggestedAction}
                      </Text>
                    </View>
                  ) : null}
                </SurfaceCard>
              </View>
            );
          })}

          {isSending ? (
            <View style={styles.assistantRow}>
              <SurfaceCard padding="md" style={styles.typingCard}>
                <ActivityIndicator
                  size="small"
                  color={isDark ? '#34D399' : activeTheme.primarySurface}
                />
                <Text
                  style={[
                    styles.typingText,
                    isDark && { color: '#94A3B8' },
                  ]}
                >
                  {t('common.loading')}
                </Text>
              </SurfaceCard>
            </View>
          ) : null}
        </View>

        {errorMessage ? (
          <View
            style={[
              styles.errorBanner,
              isDark && {
                backgroundColor: themeColors.expenseSoft,
                borderColor: 'rgba(248, 113, 113, 0.3)',
              },
            ]}
          >
            <AlertCircle
              size={16}
              color={isDark ? themeColors.expense : colors.expense}
              strokeWidth={1.75}
            />
            <Text
              style={[
                styles.errorText,
                isDark && { color: themeColors.expense },
              ]}
            >
              {errorMessage}
            </Text>
          </View>
        ) : null}
      </ScrollView>

      {/* Bottom Input Bar with Hard 250-char Guardrail */}
      <View
        style={[
          styles.inputDock,
          isDark && {
            backgroundColor: '#0B0F17',
            borderTopColor: 'rgba(255, 255, 255, 0.07)',
          },
        ]}
      >
        <View
          style={[
            styles.inputBox,
            isDark && {
              backgroundColor: '#131B29',
              borderColor: 'rgba(148, 163, 184, 0.2)',
            },
          ]}
        >
          <TextInput
            value={questionInput}
            onChangeText={setQuestionInput}
            placeholder={t('assistant.coach.inputPlaceholder')}
            placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
            selectionColor={isDark ? '#34D399' : activeTheme.primarySurface}
            maxLength={MAX_QUESTION_LENGTH}
            multiline
            returnKeyType="send"
            onSubmitEditing={() => void handleSend()}
            style={[
              styles.textInput,
              isDark && { color: '#F8FAFC' },
            ]}
          />

          <View style={styles.inputFooterRow}>
            <Text
              style={[
                styles.charCounter,
                typography.tabularNums,
                isDark && { color: '#94A3B8' },
              ]}
            >
              {t('assistant.coach.charCounter', { count: questionInput.length })}
            </Text>

            <Pressable
              onPress={() => void handleSend()}
              disabled={!canSend}
              accessibilityRole="button"
              accessibilityLabel={t('assistant.coach.sendAccessibility')}
              style={({ pressed }) => [
                styles.sendButton,
                {
                  backgroundColor: canSend
                    ? isDark
                      ? '#10B981'
                      : activeTheme.primarySurface
                    : isDark
                      ? '#1E293B'
                      : colors.surfaceMuted,
                  opacity: pressed && canSend ? 0.85 : 1,
                },
              ]}
            >
              {isSending ? (
                <ActivityIndicator size="small" color="#FFFFFF" />
              ) : (
                <ArrowUp
                  size={18}
                  color={
                    canSend ? '#FFFFFF' : isDark ? '#94A3B8' : colors.inkSubtle
                  }
                  strokeWidth={1.75}
                />
              )}
            </Pressable>
          </View>
        </View>
      </View>
      </View>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  historyActionBar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.sm,
  },
  historyTitle: {
    ...typography.overline,
  },
  clearPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  clearPillText: {
    fontSize: 12,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  historyLoadingWrap: {
    paddingVertical: spacing.xl,
    alignItems: 'center',
  },
  feedContent: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xl,
    gap: spacing.base,
  },
  welcomeCard: {
    gap: spacing.sm,
  },
  welcomeIcon: {
    width: 40,
    height: 40,
    borderRadius: radii.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.xs,
  },
  welcomeTitle: {
    ...typography.h3,
  },
  welcomeBody: {
    ...typography.body,
    color: colors.inkSecondary,
  },
  promptsSection: {
    gap: spacing.sm,
  },
  promptsLabel: {
    ...typography.overline,
  },
  promptsWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  promptChip: {
    paddingHorizontal: spacing.md,
    paddingVertical: 8,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  promptChipText: {
    fontSize: 13,
    fontWeight: '500',
  },
  messagesList: {
    gap: spacing.md,
  },
  userRow: {
    alignItems: 'flex-end',
  },
  userBubble: {
    maxWidth: '84%',
    paddingHorizontal: spacing.base,
    paddingVertical: spacing.md,
    borderRadius: radii.lg,
    borderBottomRightRadius: 6,
  },
  userBubbleText: {
    fontSize: 14.5,
    lineHeight: 20,
    color: colors.inkOnDark,
    fontWeight: '500',
  },
  assistantRow: {
    alignItems: 'flex-start',
  },
  assistantCard: {
    width: '100%',
    gap: spacing.md,
  },
  guardrailBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radii.full,
    backgroundColor: colors.warningSoft,
    borderWidth: 1,
    borderColor: '#FDE68A',
  },
  guardrailText: {
    fontSize: 12,
    fontWeight: '600',
    color: colors.warning,
  },
  assistantAnswer: {
    ...typography.body,
    lineHeight: 22,
  },
  categoryPill: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  categoryPillText: {
    fontSize: 12,
    fontWeight: '600',
  },
  actionBox: {
    padding: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1,
    gap: 4,
  },
  actionBoxHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  actionBoxTitle: {
    fontSize: 12.5,
    fontWeight: '700',
    letterSpacing: 0.2,
  },
  actionBoxBody: {
    fontSize: 13.5,
    lineHeight: 19,
    color: colors.ink,
  },
  typingCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  typingText: {
    ...typography.caption,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radii.md,
    backgroundColor: colors.expenseSoft,
    borderWidth: 1,
    borderColor: '#FECACA',
  },
  errorText: {
    flex: 1,
    fontSize: 13,
    color: colors.expense,
    fontWeight: '500',
  },
  inputDock: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.sm,
    paddingBottom: spacing.md,
    backgroundColor: colors.canvas,
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  inputBox: {
    borderRadius: radii.lg,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    paddingHorizontal: spacing.base,
    paddingTop: spacing.sm + 2,
    paddingBottom: spacing.sm,
    gap: spacing.xs,
  },
  textInput: {
    fontSize: 14.5,
    lineHeight: 20,
    color: colors.ink,
    maxHeight: 88,
    minHeight: 36,
    paddingVertical: 0,
  },
  inputFooterRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  charCounter: {
    fontSize: 11.5,
    color: colors.inkSubtle,
    fontWeight: '500',
  },
  sendButton: {
    width: 34,
    height: 34,
    borderRadius: radii.full,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
