import * as Haptics from 'expo-haptics';
import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';
import { Platform } from 'react-native';
import {
  createWorkspace as createWorkspaceApi,
  joinWorkspaceByCode as joinWorkspaceByCodeApi,
  listWorkspaces,
} from '@/api/endpoints';
import { tokenStorage } from '@/api/storage';
import type { UserWorkspaceMembershipDto, WorkspaceSummaryDto } from '@/api/types';
import { workspaceThemes, type WorkspaceTheme } from '@/theme/tokens';
import { useAuth } from './AuthContext';

interface WorkspaceContextValue {
  workspaces: WorkspaceSummaryDto[];
  activeWorkspace: WorkspaceSummaryDto | null;
  activeTheme: WorkspaceTheme;
  isLoadingWorkspaces: boolean;
  switchWorkspace: (workspaceId: string) => Promise<void>;
  refreshWorkspaces: () => Promise<void>;
  createHouseholdWorkspace: (name: string, defaultCurrency?: string) => Promise<WorkspaceSummaryDto>;
  joinWorkspaceWithCode: (invitationCode: string) => Promise<WorkspaceSummaryDto>;
}

const WorkspaceContext = createContext<WorkspaceContextValue | undefined>(undefined);

function mapMembershipToSummary(m: UserWorkspaceMembershipDto): WorkspaceSummaryDto {
  return {
    id: m.workspaceId,
    name: m.name,
    type: m.type,
    defaultCurrency: m.defaultCurrency,
    currentUserRole: m.role,
    createdAtUtc: m.joinedAtUtc,
    joinedAtUtc: m.joinedAtUtc,
  };
}

function resolvePreferredWorkspaceId(
  list: WorkspaceSummaryDto[],
  savedId: string | null,
): string | null {
  if (list.length === 0) {
    return null;
  }
  if (savedId && list.some((w) => w.id === savedId)) {
    return savedId;
  }
  const personal = list.find((w) => w.type === 'Personal');
  return personal?.id ?? list[0]?.id ?? null;
}

export function WorkspaceProvider({ children }: { children: React.ReactNode }) {
  const { user, isAuthenticated } = useAuth();
  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [activeWorkspaceId, setActiveWorkspaceId] = useState<string | null>(null);
  const [isLoadingWorkspaces, setIsLoadingWorkspaces] = useState(false);

  const refreshWorkspaces = useCallback(async () => {
    if (!isAuthenticated) {
      return;
    }

    setIsLoadingWorkspaces(true);
    try {
      const [fetched, savedId] = await Promise.all([
        listWorkspaces(),
        tokenStorage.getActiveWorkspaceId(),
      ]);
      setWorkspaces(fetched);
      setActiveWorkspaceId((current) => {
        const candidate = current && fetched.some((w) => w.id === current) ? current : savedId;
        return resolvePreferredWorkspaceId(fetched, candidate);
      });
    } finally {
      setIsLoadingWorkspaces(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    if (!isAuthenticated || !user) {
      setWorkspaces([]);
      setActiveWorkspaceId(null);
      return;
    }

    const initialList = user.workspaces.map(mapMembershipToSummary);
    setWorkspaces(initialList);

    let isMounted = true;
    async function syncFromServer() {
      const savedId = await tokenStorage.getActiveWorkspaceId();
      if (isMounted) {
        setActiveWorkspaceId((current) =>
          resolvePreferredWorkspaceId(initialList, current ?? savedId),
        );
      }

      try {
        const fetched = await listWorkspaces();
        if (isMounted) {
          setWorkspaces(fetched);
          setActiveWorkspaceId((current) =>
            resolvePreferredWorkspaceId(fetched, current ?? savedId),
          );
        }
      } catch {
        // Keep seeded list from /users/me if /workspaces temporarily fails.
      }
    }

    void syncFromServer();

    return () => {
      isMounted = false;
    };
  }, [isAuthenticated, user]);

  const switchWorkspace = useCallback(
    async (workspaceId: string) => {
      if (workspaceId === activeWorkspaceId) {
        return;
      }

      const exists = workspaces.some((w) => w.id === workspaceId);
      if (!exists) {
        return;
      }

      if (Platform.OS !== 'web') {
        void Haptics.selectionAsync();
      }

      setActiveWorkspaceId(workspaceId);
      await tokenStorage.saveActiveWorkspaceId(workspaceId);
    },
    [activeWorkspaceId, workspaces],
  );

  const activateWorkspace = useCallback(async (ws: WorkspaceSummaryDto) => {
    if (Platform.OS !== 'web') {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    }

    setWorkspaces((prev) =>
      prev.some((item) => item.id === ws.id) ? prev : [...prev, ws],
    );
    setActiveWorkspaceId(ws.id);
    await tokenStorage.saveActiveWorkspaceId(ws.id);
  }, []);

  const createHouseholdWorkspace = useCallback(
    async (name: string, defaultCurrency = 'TRY') => {
      const created = await createWorkspaceApi({ name, defaultCurrency });
      await activateWorkspace(created);
      return created;
    },
    [activateWorkspace],
  );

  const joinWorkspaceWithCode = useCallback(
    async (invitationCode: string) => {
      const joined = await joinWorkspaceByCodeApi(invitationCode);
      await activateWorkspace(joined);
      return joined;
    },
    [activateWorkspace],
  );

  const activeWorkspace = useMemo(
    () => workspaces.find((w) => w.id === activeWorkspaceId) ?? workspaces[0] ?? null,
    [workspaces, activeWorkspaceId],
  );

  const activeTheme = useMemo(
    () => workspaceThemes[activeWorkspace?.type ?? 'Personal'],
    [activeWorkspace?.type],
  );

  const value = useMemo<WorkspaceContextValue>(
    () => ({
      workspaces,
      activeWorkspace,
      activeTheme,
      isLoadingWorkspaces,
      switchWorkspace,
      refreshWorkspaces,
      createHouseholdWorkspace,
      joinWorkspaceWithCode,
    }),
    [
      workspaces,
      activeWorkspace,
      activeTheme,
      isLoadingWorkspaces,
      switchWorkspace,
      refreshWorkspaces,
      createHouseholdWorkspace,
      joinWorkspaceWithCode,
    ],
  );

  return (
    <WorkspaceContext.Provider value={value}>{children}</WorkspaceContext.Provider>
  );
}

export function useWorkspace(): WorkspaceContextValue {
  const ctx = useContext(WorkspaceContext);
  if (!ctx) {
    throw new Error('useWorkspace must be used within a WorkspaceProvider');
  }
  return ctx;
}
