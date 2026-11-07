import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { apiGet, apiPost, apiPostForm, ensureOk, unwrap } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { PreparedImage } from '../../../shared/lib';
import type { FlyerConfirmResult, FlyerImport, FlyerItem, FlyerItemEditInput, FlyerManualInput } from '../model/types';

export const flyerKeys = {
    all: ['flyers'] as const,
    latest: (menuId: string) => [...flyerKeys.all, 'latest', menuId] as const,
};

/** 取り込みの状態（読み取りの処理と、確定前の結果。分類はサーバーが読み直すたびに求め直す。別の端末からでも開ける） */
export const useFlyerImport = (menuId: string) =>
    useSuspenseQuery({
        queryKey: flyerKeys.latest(menuId),
        queryFn: async () => unwrap(await apiGet<FlyerImport>(ENDPOINT.FLYERS_LATEST, { menuId })),
        staleTime: 0,
    });

/**
 * 操作のあとに読み直す。isListedChanged なら献立（掲載品の写し）も読み直す（確定・確定の取り消し）
 * 行を直しただけでも、ほかの行の分類・まとめの件数が変わることがあるので、取り込みの結果は全体を読み直す
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

/** 行を直す（確定はしない。サーバーが分類し直した行を返す。09 の 5.3） */
export const useUpdateFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
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

/** 使えない（入数が分からない）の行に入数を入れて、分類し直す（FR-IMPORT-11。この行はまとめに入らない） */
export const useEnterFlyerPieces = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async ({ itemId, piecesPerPack }: { itemId: string; piecesPerPack: number }) =>
            unwrap(await apiPost<FlyerItem>(ENDPOINT.FLYERS_ENTER_PIECES, { menuId, itemId, piecesPerPack })),
        onSuccess: invalidate,
    });
};

/** 再入力の理由に「このままでよい」を押す（取り込みのメモリにだけ残る。記録には使わない） */
export const useAcceptFlyerReason = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async ({ itemId, reasonKey }: { itemId: string; reasonKey: string }) =>
            unwrap(await apiPost<FlyerItem>(ENDPOINT.FLYERS_ACCEPT_REASON, { menuId, itemId, reasonKey })),
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

/** 1商品を確定する（使える → 一致した商品に、要確認 → 新しい商品として登録。どちらかはサーバーが分類で決める） */
export const useConfirmFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async (itemId: string) => unwrap(await apiPost<FlyerConfirmResult>(ENDPOINT.FLYERS_CONFIRM, { menuId, itemId })),
        onSuccess: invalidate,
    });
};

/** まとめての確定の入力（itemIds は確かめのダイアログの行。editingItemIds は表で行を開いて直している途中の商品で、まとめから外す） */
export interface FlyerBulkConfirmInput {
    itemIds: string[];
    editingItemIds: string[];
}

/** 使える商品をまとめて確定する（条件はサーバーが確かめ直す。09 の 5.2） */
export const useConfirmUsableFlyerItems = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async (input: FlyerBulkConfirmInput) => unwrap(await apiPost<FlyerConfirmResult>(ENDPOINT.FLYERS_CONFIRM_USABLE, { menuId, ...input })),
        onSuccess: invalidate,
    });
};

/** 新しい商品をまとめて登録する（条件はサーバーが確かめ直す。D-98・09 の 5.2） */
export const useConfirmNewFlyerItems = (menuId: string) => {
    const invalidate = useInvalidate(menuId, true);
    return useMutation({
        mutationFn: async (input: FlyerBulkConfirmInput) => unwrap(await apiPost<FlyerConfirmResult>(ENDPOINT.FLYERS_CONFIRM_NEW, { menuId, ...input })),
        onSuccess: invalidate,
    });
};

/** 「表記なし」と読んだ商品を、チラシで見比べたことを残す（印を付けた件数を返す。D-97） */
export const useMarkLabelAbsentCompared = (menuId: string) => {
    const invalidate = useInvalidate(menuId, false);
    return useMutation({
        mutationFn: async (itemIds: string[]) => unwrap(await apiPost<number>(ENDPOINT.FLYERS_MARK_LABEL_ABSENT_COMPARED, { menuId, itemIds })),
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
