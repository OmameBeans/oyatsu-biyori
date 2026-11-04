import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { apiGet, apiPost, apiPostForm, ensureOk, unwrap } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { PreparedImage } from '../../../shared/lib';
import type { FlyerConfirmInput, FlyerImport, FlyerManualInput } from '../model/types';

export const flyerKeys = {
    all: ['flyers'] as const,
    latest: (menuId: string) => [...flyerKeys.all, 'latest', menuId] as const,
};

/** 取り込みの状態（読み取りの処理と、確定前の結果。別の端末からでも開ける） */
export const useFlyerImport = (menuId: string) =>
    useSuspenseQuery({
        queryKey: flyerKeys.latest(menuId),
        queryFn: async () => unwrap(await apiGet<FlyerImport>(ENDPOINT.FLYERS_LATEST, { menuId })),
        staleTime: 0,
    });

/** 確定したら、取り込みの結果と献立（掲載品）を読み直す */
const useInvalidate = (menuId: string) => {
    const queryClient = useQueryClient();
    return () => {
        void queryClient.invalidateQueries({ queryKey: flyerKeys.latest(menuId) });
        void queryClient.invalidateQueries({ queryKey: ['menus'] });
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

/** 1商品を確定する（選び直しも） */
export const useConfirmFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId);
    return useMutation({
        mutationFn: async (input: FlyerConfirmInput) => ensureOk(await apiPost<never>(ENDPOINT.FLYERS_CONFIRM, { menuId, ...input })),
        onSuccess: invalidate,
    });
};

/** 「同じ商品」をまとめて確定する */
export const useConfirmSameFlyerItems = (menuId: string) => {
    const invalidate = useInvalidate(menuId);
    return useMutation({
        mutationFn: async () => unwrap(await apiPost<number>(ENDPOINT.FLYERS_CONFIRM_SAME, { menuId })),
        onSuccess: invalidate,
    });
};

/** 手入力の商品を足す */
export const useAddManualFlyerItem = (menuId: string) => {
    const invalidate = useInvalidate(menuId);
    return useMutation({
        mutationFn: async (input: FlyerManualInput) => unwrap(await apiPost<string>(ENDPOINT.FLYERS_ADD_MANUAL, { menuId, ...input })),
        onSuccess: invalidate,
    });
};

/** 確定前の読み取り結果を捨てる（取り込み直し） */
export const useDiscardFlyerImport = (menuId: string) => {
    const invalidate = useInvalidate(menuId);
    return useMutation({
        mutationFn: async () => ensureOk(await apiPost<never>(ENDPOINT.FLYERS_DISCARD, { menuId })),
        onSuccess: invalidate,
    });
};

/** 取り込みの状態を読み直す */
export const useRefreshFlyerImport = (menuId: string) => useInvalidate(menuId);
