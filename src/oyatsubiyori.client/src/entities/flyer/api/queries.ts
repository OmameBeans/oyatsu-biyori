import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { apiGet, apiPost, apiPostForm, ensureOk, unwrap } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { PreparedImage } from '../../../shared/lib';
import type { FlyerFinishResult, FlyerImport, FlyerItem, FlyerItemEditInput, FlyerManualInput } from '../model/types';

export const flyerKeys = {
    all: ['flyers'] as const,
    latest: (menuId: string) => [...flyerKeys.all, 'latest', menuId] as const,
};

/** 取り込みの状態（読み取りの処理と、取り込みを終える前の結果。分類はサーバーが読み直すたびに求め直す。別の端末からでも開ける） */
export const useFlyerImport = (menuId: string) =>
    useSuspenseQuery({
        queryKey: flyerKeys.latest(menuId),
        queryFn: async () => unwrap(await apiGet<FlyerImport>(ENDPOINT.FLYERS_LATEST, { menuId })),
        staleTime: 0,
    });

/**
 * 操作のあとに読み直す。isListedChanged なら献立（掲載品の写し）も読み直す（取り込みを終える・取り込みの取り消し）
 * 行を直しただけでも、ほかの行の分類・件数が変わることがあるので、取り込みの結果は全体を読み直す
 */
const useInvalidate = (menuId: string, isListedChanged: boolean) => {
    const queryClient = useQueryClient();
    return () => {
        void queryClient.invalidateQueries({ queryKey: flyerKeys.latest(menuId) });
        if (isListedChanged) {
            void queryClient.invalidateQueries({ queryKey: ['menus'] });
        }
    };
};

/** 読み取りを受け付ける（画像は FormData で送る。送ったあともメモリに残し、失敗したら「もう一度試す」に使う） */
export const useStartFlyerReading = (menuId: string) =>
    useMutation({
        mutationFn: async (images: PreparedImage[]) => {
            const form = new FormData();
            form.append('menuId', menuId);
            images.forEach((image, index) => form.append('files', image.blob, `flyer-${index + 1}.jpg`));
            return unwrap(await apiPostForm<string>(ENDPOINT.FLYERS_READ, form));
        },
    });

/** 行を直す（サーバーが分類し直した行を返す。09 の 5.3）。取り込み済みの行を直すと取り込みが取り消されて写しが変わるので、献立も読み直す */
export const useUpdateFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async (input: FlyerItemEditInput) => unwrap(await apiPost<FlyerItem>(ENDPOINT.FLYERS_UPDATE_ITEM, { menuId, ...input })),
        onSuccess: invalidate,
    });
};

/** 品目の分からない語に品目を選ぶ（allergenIds が空なら「対応する品目なし」。選び直しも同じ） */
export const useResolveFlyerTerm = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async ({ itemId, term, allergenIds }: { itemId: string; term: string; allergenIds: string[] }) =>
            unwrap(await apiPost<FlyerItem>(ENDPOINT.FLYERS_RESOLVE_TERM, { menuId, itemId, term, allergenIds })),
        onSuccess: invalidate,
    });
};

/** 使えない（入数が分からない）の行に入数を入れて、分類し直す（FR-IMPORT-11） */
export const useEnterFlyerPieces = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async ({ itemId, piecesPerPack }: { itemId: string; piecesPerPack: number }) =>
            unwrap(await apiPost<FlyerItem>(ENDPOINT.FLYERS_ENTER_PIECES, { menuId, itemId, piecesPerPack })),
        onSuccess: invalidate,
    });
};

/** 外す（外した掲載品に移す。確定済みの行は、確定を取り消してから外す。FR-IMPORT-21） */
export const useRemoveFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async (itemId: string) => ensureOk(await apiPost<never>(ENDPOINT.FLYERS_REMOVE, { menuId, itemId })),
        onSuccess: invalidate,
    });
};

/** 外した掲載品を一覧に戻す（分類し直す） */
export const useRestoreFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async (itemId: string) => unwrap(await apiPost<FlyerItem>(ENDPOINT.FLYERS_RESTORE, { menuId, itemId })),
        onSuccess: invalidate,
    });
};

/**
 * 取り込みを終える（取り込めるの商品をまとめて取り込む。新しい商品は登録し、登録済みの商品は価格などを上書きする。D-101）。
 * どれを取り込むかはサーバーが分類し直して決める（使えない・再入力は取り込まない）。0件でも食い違いの記録のために押せる
 */
export const useFinishFlyerImport = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async () => unwrap(await apiPost<FlyerFinishResult>(ENDPOINT.FLYERS_FINISH, { menuId })),
        onSuccess: invalidate,
    });
};

/** 手入力の商品を足す */
export const useAddManualFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async (input: FlyerManualInput) => unwrap(await apiPost<string>(ENDPOINT.FLYERS_ADD_MANUAL, { menuId, ...input })),
        onSuccess: invalidate,
    });
};

/** 確定前の読み取り結果を捨てる（取り込み直し） */
export const useDiscardFlyerImport = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async () => ensureOk(await apiPost<never>(ENDPOINT.FLYERS_DISCARD, { menuId })),
        onSuccess: invalidate,
    });
};

/** 取り込みの状態を読み直す */
export const useRefreshFlyerImport = (menuId: string) => useInvalidate(menuId, true);
