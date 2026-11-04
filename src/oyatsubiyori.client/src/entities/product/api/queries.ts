import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { apiGet, apiPost, apiPostForm, unwrap } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { PreparedImage } from '../../../shared/lib';
import type { CheckRecorded, CheckRecordInput, KeywordRange, ProductDetail, ProductFilter, ProductList, ProductUpdateInput } from '../model/types';

export const productKeys = {
    all: ['products'] as const,
    list: (query: string, filter: ProductFilter, includeDisabled: boolean) => [...productKeys.all, 'list', query, filter, includeDisabled] as const,
    detail: (id: string) => [...productKeys.all, 'detail', id] as const,
    keywordHits: (text: string) => [...productKeys.all, 'keyword-hits', text] as const,
};

/** 商品一覧（検索・絞り込みはサーバーで行う） */
export const useProductList = (query: string, filter: ProductFilter, includeDisabled: boolean) =>
    useSuspenseQuery({
        queryKey: productKeys.list(query, filter, includeDisabled),
        queryFn: async () => unwrap(await apiGet<ProductList>(ENDPOINT.PRODUCTS_LIST, { q: query || undefined, filter, includeDisabled })),
    });

/** 商品詳細 */
export const useProduct = (id: string) =>
    useSuspenseQuery({
        queryKey: productKeys.detail(id),
        queryFn: async () => unwrap(await apiGet<ProductDetail>(ENDPOINT.PRODUCTS_GET, { id })),
    });

/** 編集中の全文で探す言葉に当たった範囲（正規化はサーバーで行う。フロントで作り直さない） */
export const useKeywordHits = (text: string) =>
    useQuery({
        queryKey: productKeys.keywordHits(text),
        queryFn: async () => unwrap(await apiPost<KeywordRange[]>(ENDPOINT.PRODUCTS_KEYWORD_HITS, { text })),
        enabled: text.trim().length > 0,
        placeholderData: (previous) => previous,
    });

/** 判定が変わるため、商品・献立の両方を読み直す */
const useInvalidateAll = () => {
    const queryClient = useQueryClient();
    return () => {
        void queryClient.invalidateQueries({ queryKey: productKeys.all });
        void queryClient.invalidateQueries({ queryKey: ['menus'] });
    };
};

/** 基本情報を直す */
export const useUpdateProduct = (id: string) => {
    const queryClient = useQueryClient();
    const invalidate = useInvalidateAll();
    return useMutation({
        mutationFn: async (input: ProductUpdateInput) => unwrap(await apiPost<ProductDetail>(ENDPOINT.PRODUCTS_UPDATE, { productId: id, ...input })),
        onSuccess: (detail) => {
            queryClient.setQueryData(productKeys.detail(id), detail);
            invalidate();
        },
    });
};

/** 原材料表示の読み取りを受け付ける（画像は FormData で送る。送ったあともメモリに残し、失敗したら「もう一度試す」に使う） */
export const useStartExtraction = (id: string) =>
    useMutation({
        mutationFn: async (images: PreparedImage[]) => {
            const form = new FormData();
            form.append('productId', id);
            images.forEach((image, index) => form.append('files', image.blob, `package-${index + 1}.jpg`));
            return unwrap(await apiPostForm<string>(ENDPOINT.PRODUCTS_EXTRACT, form));
        },
    });

/** アレルゲン確認の記録を足す（追加のみ） */
export const useRecordCheck = (id: string) => {
    const invalidate = useInvalidateAll();
    return useMutation({
        mutationFn: async (input: CheckRecordInput) => unwrap(await apiPost<CheckRecorded>(ENDPOINT.PRODUCTS_RECORD_CHECK, { productId: id, ...input })),
        onSuccess: invalidate,
    });
};
