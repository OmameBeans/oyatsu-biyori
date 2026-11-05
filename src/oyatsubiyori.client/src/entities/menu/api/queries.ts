import { useIsMutating, useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { ensureOk, isJobFinished, unwrap, useJobPolling } from '../../../shared/api';
import type { GenericResult } from '../../../shared/type';
import type { AcknowledgementInput, BudgetFitOption, MenuDetail, MenuProposalOutcome } from '../model/types';
import {
    fetchConfirmation,
    fetchLatestProposalJob,
    fetchListedProducts,
    fetchMenu,
    fetchMenuList,
    postAcknowledge,
    postAddItem,
    postApplyBudgetFit,
    postBudgetFit,
    postChat,
    postConfirm,
    postCreateMenu,
    postDeleteMenu,
    postMoveItem,
    postRemoveItem,
    postReplaceItem,
    postSaveRequest,
    postSetExcluded,
    postSetListedValues,
    postStartProposal,
    postUpdateBudget,
    postUpdateDay,
    postUpdateItem,
} from './menu';
import type { DayUpdate, ItemUpdate } from './menu';

export const menuKeys = {
    all: ['menus'] as const,
    lists: () => [...menuKeys.all, 'list'] as const,
    detail: (id: string) => [...menuKeys.all, 'detail', id] as const,
    listed: (id: string) => [...menuKeys.all, 'listed', id] as const,
    confirmation: (id: string) => [...menuKeys.all, 'confirmation', id] as const,
    proposalJob: (id: string) => [...menuKeys.all, 'proposal-job', id] as const,
    edit: (id: string) => [...menuKeys.all, 'edit', id] as const,
};

/** 献立一覧 */
export const useMenuList = () =>
    useSuspenseQuery({
        queryKey: menuKeys.lists(),
        queryFn: async () => unwrap(await fetchMenuList()),
    });

/** 献立の中身 */
export const useMenu = (id: string) =>
    useSuspenseQuery({
        queryKey: menuKeys.detail(id),
        queryFn: async () => unwrap(await fetchMenu(id)),
    });

/** 手順2 今週の商品 */
export const useListedProducts = (id: string) =>
    useSuspenseQuery({
        queryKey: menuKeys.listed(id),
        queryFn: async () => unwrap(await fetchListedProducts(id)),
    });

/** 確定のダイアログの中身（開いたときに読み直す） */
export const useMenuConfirmation = (id: string, isEnabled: boolean) =>
    useQuery({
        queryKey: menuKeys.confirmation(id),
        queryFn: async () => unwrap(await fetchConfirmation(id)),
        enabled: isEnabled,
        staleTime: 0,
    });

/** 献立案の作成・組み直しの、いちばん新しい処理（再開時） */
export const useLatestProposalJob = (id: string) =>
    useSuspenseQuery({
        queryKey: menuKeys.proposalJob(id),
        queryFn: async () => unwrap(await fetchLatestProposalJob(id)),
        staleTime: 0,
    });

/** 献立の下書きを作る */
export const useCreateMenu = () => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (weekStart: string) => unwrap(await postCreateMenu(weekStart)),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: menuKeys.lists() }),
    });
};

/** 下書きを削除する */
export const useDeleteMenu = () => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (menuId: string) => ensureOk(await postDeleteMenu(menuId)),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: menuKeys.all }),
    });
};

/** 献立に関わるものを読み直す */
const useInvalidateMenu = (menuId: string) => {
    const queryClient = useQueryClient();
    return () => queryClient.invalidateQueries({ queryKey: menuKeys.all, predicate: (q) => q.queryKey.includes(menuId) || q.queryKey[1] === 'list' });
};

/** 献立を返す操作：返った献立で画面を置き換える */
const useMenuMutation = <TVariables>(menuId: string, action: (variables: TVariables) => Promise<GenericResult<MenuDetail>>) => {
    const queryClient = useQueryClient();
    const invalidate = useInvalidateMenu(menuId);
    return useMutation({
        mutationKey: menuKeys.edit(menuId),
        mutationFn: async (variables: TVariables) => unwrap(await action(variables)),
        onSuccess: (detail) => {
            queryClient.setQueryData(menuKeys.detail(menuId), detail);
            void queryClient.invalidateQueries({ queryKey: menuKeys.lists() });
            void queryClient.invalidateQueries({ queryKey: menuKeys.listed(menuId) });
        },
        onError: () => {
            void invalidate();
        },
    });
};

/** 値を返さない操作：終わったら読み直す */
const useMenuAction = <TVariables, TResult>(menuId: string, action: (variables: TVariables) => Promise<GenericResult<TResult>>) => {
    const invalidate = useInvalidateMenu(menuId);
    return useMutation({
        mutationFn: async (variables: TVariables) => unwrap(await action(variables)),
        onSettled: () => invalidate(),
    });
};

export const useSetExcluded = (menuId: string) =>
    useMenuAction(menuId, ({ productId, excluded }: { productId: string; excluded: boolean }) => postSetExcluded(menuId, productId, excluded));
export const useSetListedValues = (menuId: string) =>
    useMenuAction(menuId, ({ productId, price, piecesPerPack }: { productId: string; price: number | null; piecesPerPack: number | null }) =>
        postSetListedValues(menuId, productId, price, piecesPerPack),
    );
export const useSaveRequest = (menuId: string) => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (requestText: string) => ensureOk(await postSaveRequest(menuId, requestText)),
        onSuccess: (_, requestText) => {
            // 保存で row_version が変わるため、献立を読み直す（古い値のまま次の編集をすると衝突になる）
            queryClient.setQueryData<MenuDetail>(menuKeys.detail(menuId), (old) => (old ? { ...old, requestText } : old));
            void queryClient.invalidateQueries({ queryKey: menuKeys.detail(menuId) });
        },
    });
};
/**
 * 献立案の作成・チャットの組み直しが動いているか（手で直させないために、各列がそれぞれ読む）。
 * 動いている処理の状態は、献立の列・チャットと同じクエリ（2秒ごとに確かめる）を使うので、問い合わせは増えない
 */
export const useIsMenuProposalRunning = (menuId: string) => {
    const { data: latest } = useLatestProposalJob(menuId);
    const jobId = latest.job && !isJobFinished(latest.job) ? latest.job.id : null;
    const chatJobId = latest.chatJob && !isJobFinished(latest.chatJob) ? latest.chatJob.id : null;
    const { data: job } = useJobPolling<MenuProposalOutcome>(jobId);
    const { data: chatJob } = useJobPolling<MenuProposalOutcome>(chatJobId);
    return (!!jobId && !isJobFinished(job)) || (!!chatJobId && !isJobFinished(chatJob));
};

/** 献立案の作成・組み直しを始めたら、動いている処理を読み直す（献立の画面とチャットの両方で、終わるまで直せなくする） */
export const useStartProposal = (menuId: string) => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (requestText: string) => unwrap(await postStartProposal(menuId, requestText)),
        onSuccess: () => queryClient.invalidateQueries({ queryKey: menuKeys.proposalJob(menuId) }),
    });
};
export const useChat = (menuId: string) => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (message: string) => unwrap(await postChat(menuId, message)),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: menuKeys.proposalJob(menuId) });
            void queryClient.invalidateQueries({ queryKey: menuKeys.detail(menuId) });
        },
    });
};
export const useStartBudgetFit = (menuId: string) =>
    useMutation({
        mutationFn: async () => unwrap(await postBudgetFit(menuId)),
    });
export const useApplyBudgetFit = (menuId: string) => useMenuMutation(menuId, (option: BudgetFitOption) => postApplyBudgetFit(menuId, option));
export const useUpdateBudget = (menuId: string) =>
    useMenuMutation(menuId, ({ budget, rowVersion }: { budget: number | null; rowVersion: string }) => postUpdateBudget(menuId, budget, rowVersion));
export const useUpdateDay = (menuId: string) =>
    useMenuMutation(menuId, ({ update, rowVersion }: { update: DayUpdate; rowVersion: string }) => postUpdateDay(menuId, update, rowVersion));
export const useAddItem = (menuId: string) =>
    useMenuMutation(menuId, ({ update, rowVersion }: { update: ItemUpdate; rowVersion: string }) => postAddItem(menuId, update, rowVersion));
export const useReplaceItem = (menuId: string) =>
    useMenuMutation(menuId, ({ update, rowVersion }: { update: ItemUpdate; rowVersion: string }) => postReplaceItem(menuId, update, rowVersion));
export const useRemoveItem = (menuId: string) =>
    useMenuMutation(menuId, ({ update, rowVersion }: { update: ItemUpdate; rowVersion: string }) => postRemoveItem(menuId, update, rowVersion));
/** 商品を別の日へ動かす（ドラッグ＆ドロップ） */
export const useMoveItem = (menuId: string) =>
    useMenuMutation(menuId, ({ update, rowVersion }: { update: ItemUpdate; rowVersion: string }) => postMoveItem(menuId, update, rowVersion));
export const useUpdateItem = (menuId: string) =>
    useMenuMutation(menuId, ({ update, rowVersion }: { update: ItemUpdate; rowVersion: string }) => postUpdateItem(menuId, update, rowVersion));
export const useConfirmMenu = (menuId: string) =>
    useMenuMutation(menuId, ({ acknowledgements, rowVersion }: { acknowledgements: AcknowledgementInput[]; rowVersion: string }) =>
        postConfirm(menuId, acknowledgements, rowVersion),
    );
export const useAcknowledge = (menuId: string) => useMenuMutation(menuId, (input: AcknowledgementInput) => postAcknowledge(menuId, input));

/**
 * 献立を直す操作（足す・動かす・外すなど）が、画面のどこかで実行中か。続けて操作すると古い row_version で送って
 * 止められるので、終わるまでドラッグなどを受け付けないために使う
 */
export const useIsMenuEditing = (menuId: string) => useIsMutating({ mutationKey: menuKeys.edit(menuId) }) > 0;

/** 献立を読み直す（処理が終わったときなど） */
export const useRefreshMenu = (menuId: string) => useInvalidateMenu(menuId);
