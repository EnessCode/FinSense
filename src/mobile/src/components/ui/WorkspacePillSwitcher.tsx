import {
  AlertCircle,
  Check,
  ChevronDown,
  Copy,
  Home,
  KeyRound,
  Plus,
  Share2,
  ShieldCheck,
  UserPlus,
  Users,
  X,
} from 'lucide-react-native';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import { createInvite, getWorkspaceMembers } from '@/api/endpoints';
import type {
  WorkspaceInvitationDto,
  WorkspaceMemberDto,
  WorkspaceRole,
} from '@/api/types';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { colors, radii, spacing, typography, workspaceThemes } from '@/theme/tokens';
import { copyOrShareText } from '@/utils/clipboard';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { AppButton } from './AppButton';
import { AppInput } from './AppInput';

type SheetPanel = 'list' | 'create' | 'join';
type AssignableRole = Exclude<WorkspaceRole, 'Owner'>;
const ASSIGNABLE_ROLES: AssignableRole[] = ['Admin', 'Contributor', 'Viewer'];

export function WorkspacePillSwitcher() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { colors: themeColors, isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();
  const {
    workspaces,
    activeWorkspace,
    activeTheme,
    switchWorkspace,
    createHouseholdWorkspace,
    joinWorkspaceWithCode,
  } = useWorkspace();

  const [modalVisible, setModalVisible] = useState(false);
  const [panel, setPanel] = useState<SheetPanel>('list');

  // Create household state
  const [householdName, setHouseholdName] = useState('');
  const [nameError, setNameError] = useState<string | undefined>();

  // Join with code state
  const [inviteCodeInput, setInviteCodeInput] = useState('');
  const [codeError, setCodeError] = useState<string | undefined>();

  // Household members & invite generation
  const [members, setMembers] = useState<WorkspaceMemberDto[]>([]);
  const [isLoadingMembers, setIsLoadingMembers] = useState(false);
  const [inviteRole, setInviteRole] = useState<AssignableRole>('Contributor');
  const [generatedInvite, setGeneratedInvite] =
    useState<WorkspaceInvitationDto | null>(null);
  const [isGeneratingInvite, setIsGeneratingInvite] = useState(false);
  const [copiedFeedback, setCopiedFeedback] = useState(false);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | undefined>();

  const isHousehold = activeWorkspace?.type === 'Household';
  const canInviteMembers =
    isHousehold &&
    (activeWorkspace?.currentUserRole === 'Owner' ||
      activeWorkspace?.currentUserRole === 'Admin');

  const displayName =
    activeWorkspace?.name ??
    (isHousehold
      ? t('workspace.householdDefaultName')
      : t('workspace.personalDefaultName'));

  useEffect(() => {
    if (!modalVisible) {
      setPanel('list');
      setActionError(undefined);
      setNameError(undefined);
      setCodeError(undefined);
      setCopiedFeedback(false);
      return;
    }

    setGeneratedInvite(null);
    if (!isHousehold || !activeWorkspace?.id) {
      setMembers([]);
      return;
    }

    let isMounted = true;
    async function loadMembers() {
      setIsLoadingMembers(true);
      try {
        const res = await getWorkspaceMembers(activeWorkspace!.id);
        if (isMounted) setMembers(res);
      } catch {
        // Non-blocking in switcher sheet
      } finally {
        if (isMounted) setIsLoadingMembers(false);
      }
    }

    void loadMembers();
    return () => {
      isMounted = false;
    };
  }, [modalVisible, isHousehold, activeWorkspace?.id]);

  const handleSelect = async (workspaceId: string) => {
    await switchWorkspace(workspaceId);
    setModalVisible(false);
  };

  const handleCreateHousehold = async () => {
    setActionError(undefined);
    const trimmed = householdName.trim();
    if (!trimmed) {
      setNameError(t('workspace.createHouseholdNameRequired'));
      return;
    }

    setIsSubmitting(true);
    try {
      await createHouseholdWorkspace(trimmed, 'TRY');
      setHouseholdName('');
      setModalVisible(false);
    } catch (err) {
      setActionError(toApiError(err).message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleJoinByCode = async () => {
    setActionError(undefined);
    const cleaned = inviteCodeInput.trim().toUpperCase();
    if (cleaned.length < 6) {
      setCodeError(t('workspace.joinByCodeRequired'));
      return;
    }

    setIsSubmitting(true);
    try {
      await joinWorkspaceWithCode(cleaned);
      setInviteCodeInput('');
      setModalVisible(false);
    } catch (err) {
      setActionError(toApiError(err).message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleGenerateInvite = async () => {
    if (!activeWorkspace?.id) return;
    setActionError(undefined);
    setCopiedFeedback(false);
    setIsGeneratingInvite(true);
    try {
      const invite = await createInvite(activeWorkspace.id, {
        assignedRole: inviteRole,
      });
      setGeneratedInvite(invite);
    } catch (err) {
      setActionError(toApiError(err).message);
    } finally {
      setIsGeneratingInvite(false);
    }
  };

  const handleCopyInvite = async () => {
    if (!generatedInvite) return;
    const result = await copyOrShareText(generatedInvite.invitationCode);
    if (result === 'copied') {
      setCopiedFeedback(true);
      setTimeout(() => setCopiedFeedback(false), 2000);
    }
  };

  return (
    <>
      <Pressable
        onPress={() => setModalVisible(true)}
        accessibilityRole="button"
        accessibilityLabel={t('workspace.switcherTitle')}
        style={({ pressed }) => [
          styles.pill,
          {
            backgroundColor: isDark ? themeColors.surface : activeTheme.softTint,
            borderColor: isDark
              ? isHousehold
                ? 'rgba(251, 191, 36, 0.28)'
                : 'rgba(52, 211, 153, 0.28)'
              : activeTheme.softTintBorder,
            opacity: pressed ? 0.85 : 1,
          },
        ]}
      >
        <View
          style={[
            styles.iconDot,
            {
              backgroundColor: isDark
                ? isHousehold
                  ? '#D97706'
                  : '#10B981'
                : activeTheme.primarySurface,
            },
          ]}
        >
          {isHousehold ? (
            <Users size={12} color="#FFFFFF" strokeWidth={1.75} />
          ) : (
            <ShieldCheck size={12} color="#FFFFFF" strokeWidth={1.75} />
          )}
        </View>

        <Text
          numberOfLines={1}
          style={[
            styles.pillText,
            { color: isDark ? themeColors.ink : activeTheme.primarySurface },
          ]}
        >
          {displayName}
        </Text>

        <ChevronDown
          size={15}
          color={isDark ? themeColors.inkSecondary : activeTheme.primarySurface}
          strokeWidth={1.75}
        />
      </Pressable>

      <Modal
        visible={modalVisible}
        transparent
        statusBarTranslucent
        navigationBarTranslucent
        animationType="fade"
        onRequestClose={() => setModalVisible(false)}
      >
        <KeyboardAvoidingView
          style={styles.modalBackdrop}
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        >
          <Pressable
            style={StyleSheet.absoluteFill}
            onPress={() => setModalVisible(false)}
          />

          <View
            style={[
              styles.sheet,
              {
                backgroundColor: themeColors.surface,
                borderColor: themeColors.cardBorder,
                paddingBottom:
                  Platform.OS === 'android' && keyboardHeight > 0
                    ? keyboardHeight + spacing.md
                    : Math.max(insets.bottom, 20) + spacing.sm,
              },
            ]}
          >
            <View
              style={[
                styles.sheetHandle,
                { backgroundColor: themeColors.border },
              ]}
            />

            <View style={styles.sheetHeader}>
              <View style={styles.sheetTitleGroup}>
                <Text style={[styles.sheetTitle, { color: themeColors.ink }]}>
                  {panel === 'create'
                    ? t('workspace.createHouseholdTitle')
                    : panel === 'join'
                      ? t('workspace.joinByCodeTitle')
                      : t('workspace.switcherTitle')}
                </Text>
                <Text
                  style={[
                    styles.sheetSubtitle,
                    { color: themeColors.inkSecondary },
                  ]}
                >
                  {panel === 'join'
                    ? t('workspace.joinByCodeHint')
                    : t('workspace.switcherSubtitle')}
                </Text>
              </View>

              <Pressable
                onPress={() => setModalVisible(false)}
                hitSlop={12}
                style={[
                  styles.closeButton,
                  {
                    backgroundColor: themeColors.surfaceSubtle,
                    borderColor: themeColors.border,
                  },
                ]}
              >
                <X
                  size={18}
                  color={themeColors.inkSecondary}
                  strokeWidth={1.75}
                />
              </Pressable>
            </View>

            {actionError ? (
              <View style={styles.errorBanner}>
                <AlertCircle size={16} color={colors.expense} strokeWidth={1.75} />
                <Text style={styles.errorBannerText}>{actionError}</Text>
              </View>
            ) : null}

            <ScrollView
              bounces={false}
              keyboardShouldPersistTaps="handled"
              showsVerticalScrollIndicator={false}
              contentContainerStyle={styles.scrollBody}
            >
              {panel === 'list' ? (
                <>
                  <View style={styles.workspaceList}>
                    {workspaces.map((ws) => {
                      const isSelected = ws.id === activeWorkspace?.id;
                      const theme = workspaceThemes[ws.type];
                      const wsIsHousehold = ws.type === 'Household';
                      const darkAccent = wsIsHousehold ? '#FBBF24' : '#10B981';

                      return (
                        <Pressable
                          key={ws.id}
                          onPress={() => void handleSelect(ws.id)}
                          style={({ pressed }) => [
                            styles.workspaceItem,
                            isDark && {
                              backgroundColor: '#182234',
                              borderColor: isSelected
                                ? darkAccent
                                : 'rgba(148, 163, 184, 0.22)',
                            },
                            !isDark &&
                              isSelected && {
                                backgroundColor: theme.softTint,
                                borderColor: theme.softTintBorder,
                              },
                            pressed && styles.workspaceItemPressed,
                          ]}
                        >
                          <View
                            style={[
                              styles.workspaceAvatar,
                              {
                                backgroundColor: isDark
                                  ? wsIsHousehold
                                    ? '#D97706'
                                    : '#10B981'
                                  : theme.primarySurface,
                              },
                            ]}
                          >
                            {wsIsHousehold ? (
                              <Home size={18} color="#FFFFFF" strokeWidth={1.75} />
                            ) : (
                              <ShieldCheck
                                size={18}
                                color="#FFFFFF"
                                strokeWidth={1.75}
                              />
                            )}
                          </View>

                          <View style={styles.workspaceMeta}>
                            <Text
                              numberOfLines={1}
                              style={[
                                styles.workspaceName,
                                isDark && { color: '#F8FAFC' },
                              ]}
                            >
                              {ws.name}
                            </Text>
                            <Text
                              style={[
                                styles.workspaceDetail,
                                isDark && { color: '#94A3B8' },
                              ]}
                            >
                              {wsIsHousehold
                                ? t('workspace.householdType')
                                : t('workspace.personalType')}{' '}
                              • {t(`workspace.roles.${ws.currentUserRole}`)}
                            </Text>
                          </View>

                          {isSelected ? (
                            <View
                              style={[
                                styles.checkBadge,
                                {
                                  backgroundColor: isDark
                                    ? darkAccent
                                    : theme.primarySurface,
                                },
                              ]}
                            >
                              <Check size={14} color="#FFFFFF" strokeWidth={1.75} />
                            </View>
                          ) : null}
                        </Pressable>
                      );
                    })}
                  </View>

                  {/* Household Actions */}
                  <View style={styles.actionButtonsRow}>
                    <Pressable
                      onPress={() => {
                        setActionError(undefined);
                        setPanel('create');
                      }}
                      style={({ pressed }) => [
                        styles.secondaryActionBtn,
                        isDark && {
                          backgroundColor: '#182234',
                          borderColor: 'rgba(148, 163, 184, 0.22)',
                        },
                        pressed && styles.workspaceItemPressed,
                      ]}
                    >
                      <Plus
                        size={16}
                        color={
                          isDark
                            ? '#FBBF24'
                            : workspaceThemes.Household.primarySurface
                        }
                        strokeWidth={1.75}
                      />
                      <Text
                        style={[
                          styles.secondaryActionText,
                          {
                            color: isDark
                              ? '#F8FAFC'
                              : workspaceThemes.Household.primarySurface,
                          },
                        ]}
                      >
                        {t('workspace.createHouseholdButton')}
                      </Text>
                    </Pressable>

                    <Pressable
                      onPress={() => {
                        setActionError(undefined);
                        setPanel('join');
                      }}
                      style={({ pressed }) => [
                        styles.secondaryActionBtn,
                        isDark && {
                          backgroundColor: '#182234',
                          borderColor: 'rgba(148, 163, 184, 0.22)',
                        },
                        pressed && styles.workspaceItemPressed,
                      ]}
                    >
                      <KeyRound
                        size={16}
                        color={isDark ? '#34D399' : colors.ink}
                        strokeWidth={1.75}
                      />
                      <Text
                        style={[
                          styles.secondaryActionText,
                          isDark && { color: '#F8FAFC' },
                        ]}
                      >
                        {t('workspace.joinByCodeButton')}
                      </Text>
                    </Pressable>
                  </View>

                  {/* Household Members & Invite Generator */}
                  {isHousehold ? (
                    <View style={styles.householdSection}>
                      <View
                        style={[
                          styles.divider,
                          isDark && {
                            backgroundColor: 'rgba(255, 255, 255, 0.08)',
                          },
                        ]}
                      />

                      <View style={styles.membersBlock}>
                        <Text
                          style={[
                            styles.sectionOverline,
                            isDark && { color: '#94A3B8' },
                          ]}
                        >
                          {t('workspace.membersTitle')}
                        </Text>

                        {isLoadingMembers ? (
                          <ActivityIndicator
                            size="small"
                            color={
                              isDark ? '#34D399' : activeTheme.primarySurface
                            }
                          />
                        ) : (
                          <View style={styles.memberList}>
                            {members.map((member) => (
                              <View
                                key={member.userId}
                                style={[
                                  styles.memberRow,
                                  isDark && {
                                    backgroundColor: '#182234',
                                    borderColor: 'rgba(148, 163, 184, 0.22)',
                                  },
                                ]}
                              >
                                <View style={styles.memberInfo}>
                                  <Text
                                    style={[
                                      styles.memberName,
                                      isDark && { color: '#F8FAFC' },
                                    ]}
                                    numberOfLines={1}
                                  >
                                    {member.displayName}
                                  </Text>
                                  <Text
                                    style={[
                                      styles.memberEmail,
                                      isDark && { color: '#94A3B8' },
                                    ]}
                                    numberOfLines={1}
                                  >
                                    {member.email}
                                  </Text>
                                </View>
                                <View
                                  style={[
                                    styles.roleTag,
                                    {
                                      backgroundColor: isDark
                                        ? 'rgba(251, 191, 36, 0.14)'
                                        : activeTheme.softTint,
                                      borderColor: isDark
                                        ? 'rgba(251, 191, 36, 0.28)'
                                        : activeTheme.softTintBorder,
                                    },
                                  ]}
                                >
                                  <Text
                                    style={[
                                      styles.roleTagText,
                                      {
                                        color: isDark
                                          ? '#FBBF24'
                                          : activeTheme.primarySurface,
                                      },
                                    ]}
                                  >
                                    {t(`workspace.roles.${member.role}`)}
                                  </Text>
                                </View>
                              </View>
                            ))}
                          </View>
                        )}
                      </View>

                      {canInviteMembers ? (
                        <View style={styles.inviteBlock}>
                          <Text
                            style={[
                              styles.sectionOverline,
                              isDark && { color: '#94A3B8' },
                            ]}
                          >
                            {t('workspace.inviteMemberTitle')}
                          </Text>

                          <View style={styles.roleChipsRow}>
                            {ASSIGNABLE_ROLES.map((r) => {
                              const selected = inviteRole === r;
                              return (
                                <Pressable
                                  key={r}
                                  onPress={() => setInviteRole(r)}
                                  style={[
                                    styles.roleChip,
                                    isDark &&
                                      !selected && {
                                        backgroundColor: '#182234',
                                        borderColor: 'rgba(148, 163, 184, 0.22)',
                                      },
                                    selected && {
                                      backgroundColor: isDark
                                        ? '#D97706'
                                        : activeTheme.softTint,
                                      borderColor: isDark
                                        ? '#FBBF24'
                                        : activeTheme.primarySurface,
                                    },
                                  ]}
                                >
                                  <Text
                                    style={[
                                      styles.roleChipText,
                                      isDark && !selected && { color: '#E2E8F0' },
                                      selected && {
                                        color: isDark
                                          ? '#FFFFFF'
                                          : activeTheme.primarySurface,
                                        fontWeight: '600',
                                      },
                                    ]}
                                  >
                                    {t(`workspace.roles.${r}`)}
                                  </Text>
                                </Pressable>
                              );
                            })}
                          </View>

                          {generatedInvite ? (
                            <View
                              style={[
                                styles.generatedCodeCard,
                                {
                                  backgroundColor: isDark
                                    ? '#182234'
                                    : activeTheme.softTint,
                                  borderColor: isDark
                                    ? 'rgba(251, 191, 36, 0.35)'
                                    : activeTheme.softTintBorder,
                                },
                              ]}
                            >
                              <Text
                                style={[
                                  styles.generatedLabel,
                                  isDark && { color: '#94A3B8' },
                                ]}
                              >
                                {t('workspace.generatedCodeLabel')}
                              </Text>
                              <Text
                                selectable
                                style={[
                                  styles.generatedCodeText,
                                  typography.tabularNums,
                                  {
                                    color: isDark
                                      ? '#F8FAFC'
                                      : activeTheme.primarySurface,
                                  },
                                ]}
                              >
                                {generatedInvite.invitationCode}
                              </Text>

                              <Pressable
                                onPress={() => void handleCopyInvite()}
                                style={[
                                  styles.copyBtn,
                                  {
                                    backgroundColor: isDark
                                      ? '#D97706'
                                      : activeTheme.primarySurface,
                                  },
                                ]}
                              >
                                {Platform.OS === 'web' ? (
                                  <Copy size={14} color="#FFFFFF" strokeWidth={1.75} />
                                ) : (
                                  <Share2 size={14} color="#FFFFFF" strokeWidth={1.75} />
                                )}
                                <Text style={styles.copyBtnText}>
                                  {copiedFeedback
                                    ? t('workspace.copiedCode')
                                    : Platform.OS === 'web'
                                      ? t('workspace.copyCode')
                                      : t('workspace.shareCode')}
                                </Text>
                              </Pressable>
                            </View>
                          ) : (
                            <AppButton
                              label={t('workspace.generateCodeButton')}
                              onPress={() => void handleGenerateInvite()}
                              loading={isGeneratingInvite}
                              primaryColor={
                                isDark ? '#D97706' : activeTheme.primarySurface
                              }
                              leftIcon={
                                <UserPlus
                                  size={16}
                                  color="#FFFFFF"
                                  strokeWidth={1.75}
                                />
                              }
                            />
                          )}
                        </View>
                      ) : null}
                    </View>
                  ) : null}
                </>
              ) : panel === 'create' ? (
                <View style={styles.subPanelForm}>
                  <AppInput
                    label={t('workspace.createHouseholdTitle')}
                    placeholder={t('workspace.createHouseholdPlaceholder')}
                    value={householdName}
                    onChangeText={(text) => {
                      setHouseholdName(text);
                      if (nameError) setNameError(undefined);
                    }}
                    error={nameError}
                    accentColor={workspaceThemes.Household.primarySurface}
                    autoFocus
                  />

                  <View style={styles.subPanelActions}>
                    <AppButton
                      label={t('common.cancel')}
                      variant="secondary"
                      onPress={() => setPanel('list')}
                      style={styles.subPanelBtn}
                    />
                    <AppButton
                      label={t('common.create')}
                      onPress={() => void handleCreateHousehold()}
                      loading={isSubmitting}
                      primaryColor={workspaceThemes.Household.primarySurface}
                      style={styles.subPanelBtn}
                    />
                  </View>
                </View>
              ) : (
                <View style={styles.subPanelForm}>
                  <AppInput
                    label={t('workspace.joinByCodeTitle')}
                    placeholder={t('workspace.joinByCodePlaceholder')}
                    value={inviteCodeInput}
                    onChangeText={(text) => {
                      setInviteCodeInput(text.toUpperCase());
                      if (codeError) setCodeError(undefined);
                    }}
                    error={codeError}
                    autoCapitalize="characters"
                    autoCorrect={false}
                    maxLength={12}
                    accentColor={workspaceThemes.Household.primarySurface}
                    autoFocus
                  />

                  <View style={styles.subPanelActions}>
                    <AppButton
                      label={t('common.cancel')}
                      variant="secondary"
                      onPress={() => setPanel('list')}
                      style={styles.subPanelBtn}
                    />
                    <AppButton
                      label={t('workspace.joinButton')}
                      onPress={() => void handleJoinByCode()}
                      loading={isSubmitting}
                      primaryColor={workspaceThemes.Household.primarySurface}
                      style={styles.subPanelBtn}
                    />
                  </View>
                </View>
              )}
            </ScrollView>
          </View>
        </KeyboardAvoidingView>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  pill: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    maxWidth: 225,
    paddingVertical: 7,
    paddingLeft: 8,
    paddingRight: 12,
    borderRadius: radii.full,
    borderWidth: 1,
    gap: 8,
  },
  iconDot: {
    width: 24,
    height: 24,
    borderRadius: radii.full,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pillText: {
    fontSize: 14,
    fontWeight: '600',
    letterSpacing: -0.2,
    flexShrink: 1,
  },
  modalBackdrop: {
    flex: 1,
    backgroundColor: 'rgba(17, 19, 21, 0.42)',
    justifyContent: 'flex-end',
  },
  sheet: {
    width: '100%',
    marginBottom: 0,
    backgroundColor: colors.surface,
    borderTopLeftRadius: radii.xl,
    borderTopRightRadius: radii.xl,
    borderBottomLeftRadius: 0,
    borderBottomRightRadius: 0,
    borderTopWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
    maxHeight: '86%',
  },
  sheetHandle: {
    width: 38,
    height: 4,
    borderRadius: radii.full,
    backgroundColor: colors.border,
    alignSelf: 'center',
    marginBottom: spacing.lg,
  },
  sheetHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: spacing.base,
  },
  sheetTitleGroup: {
    flex: 1,
    paddingRight: spacing.base,
  },
  sheetTitle: {
    ...typography.h2,
    marginBottom: spacing.xs,
  },
  sheetSubtitle: {
    ...typography.caption,
    lineHeight: 18,
  },
  closeButton: {
    width: 34,
    height: 34,
    borderRadius: radii.full,
    backgroundColor: colors.surfaceSubtle,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.md,
    padding: spacing.md,
    borderRadius: radii.md,
    backgroundColor: colors.expenseSoft,
    borderWidth: 1,
    borderColor: '#FECACA',
  },
  errorBannerText: {
    flex: 1,
    fontSize: 13,
    color: colors.expense,
    fontWeight: '500',
  },
  scrollBody: {
    gap: spacing.base,
    paddingBottom: spacing.sm,
  },
  workspaceList: {
    gap: spacing.md,
  },
  workspaceItem: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: spacing.base,
    borderRadius: radii.lg,
    borderWidth: 1,
    borderColor: colors.cardBorder,
    backgroundColor: colors.canvas,
    gap: spacing.md,
  },
  workspaceItemPressed: {
    opacity: 0.85,
  },
  workspaceAvatar: {
    width: 42,
    height: 42,
    borderRadius: radii.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  workspaceMeta: {
    flex: 1,
  },
  workspaceName: {
    ...typography.h3,
    marginBottom: 2,
  },
  workspaceDetail: {
    ...typography.caption,
  },
  checkBadge: {
    width: 26,
    height: 26,
    borderRadius: radii.full,
    alignItems: 'center',
    justifyContent: 'center',
  },
  actionButtonsRow: {
    gap: spacing.sm,
  },
  secondaryActionBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    height: 46,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  secondaryActionText: {
    fontSize: 13.5,
    fontWeight: '600',
    color: colors.ink,
  },
  householdSection: {
    gap: spacing.base,
  },
  divider: {
    height: 1,
    backgroundColor: colors.cardBorder,
  },
  membersBlock: {
    gap: spacing.sm,
  },
  sectionOverline: {
    ...typography.overline,
  },
  memberList: {
    gap: spacing.sm,
  },
  memberRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.cardBorder,
    backgroundColor: colors.canvas,
  },
  memberInfo: {
    flex: 1,
  },
  memberName: {
    fontSize: 14,
    fontWeight: '600',
    color: colors.ink,
  },
  memberEmail: {
    fontSize: 12,
    color: colors.inkSecondary,
  },
  roleTag: {
    paddingHorizontal: 9,
    paddingVertical: 4,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  roleTagText: {
    fontSize: 11.5,
    fontWeight: '600',
  },
  inviteBlock: {
    gap: spacing.md,
  },
  roleChipsRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  roleChip: {
    flex: 1,
    height: 36,
    borderRadius: radii.sm,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
    alignItems: 'center',
    justifyContent: 'center',
  },
  roleChipText: {
    fontSize: 12.5,
    fontWeight: '500',
    color: colors.inkSecondary,
  },
  generatedCodeCard: {
    padding: spacing.base,
    borderRadius: radii.lg,
    borderWidth: 1,
    alignItems: 'center',
    gap: spacing.sm,
  },
  generatedLabel: {
    fontSize: 12,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  generatedCodeText: {
    fontSize: 26,
    fontWeight: '700',
    letterSpacing: 3,
  },
  copyBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: spacing.base,
    paddingVertical: 8,
    borderRadius: radii.full,
    marginTop: spacing.xs,
  },
  copyBtnText: {
    fontSize: 13,
    fontWeight: '600',
    color: '#FFFFFF',
  },
  subPanelForm: {
    gap: spacing.lg,
  },
  subPanelActions: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  subPanelBtn: {
    flex: 1,
  },
});
