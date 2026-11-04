import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { ensureOk, unwrap } from '../../../shared/api';
import type { GenericResult } from '../../../shared/type';
import type { SettingsInput } from '../model/types';
import {
    fetchAllergenMaster,
    fetchSettings,
    postAddAvoided,
    postAddGroup,
    postAddKeyword,
    postCreateFacilityAllergen,
    postDeleteFacilityAllergen,
    postRemoveAvoided,
    postRemoveKeyword,
    postSettings,
} from './settings';

export const settingsKeys = {
    all: ['settings'] as const,
    detail: () => [...settingsKeys.all, 'detail'] as const,
    allergens: () => [...settingsKeys.all, 'allergens'] as const,
};

/** 条件設定 */
export const useSettings = () =>
    useSuspenseQuery({
        queryKey: settingsKeys.detail(),
        queryFn: async () => unwrap(await fetchSettings()),
    });

/** 品目・グループ・避ける品目・探す言葉 */
export const useAllergenMaster = () =>
    useSuspenseQuery({
        queryKey: settingsKeys.allergens(),
        queryFn: async () => unwrap(await fetchAllergenMaster()),
    });

/** 条件設定を保存する */
export const useUpdateSettings = () => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (input: SettingsInput) => unwrap(await postSettings(input)),
        onSuccess: (settings) => {
            queryClient.setQueryData(settingsKeys.detail(), settings);
            void queryClient.invalidateQueries({ queryKey: ['menus'] });
        },
    });
};

/**
 * 避ける品目・探す言葉を変える操作。判定がすぐ変わるため、品目のマスタのほか、献立・商品も読み直す。
 */
const useAllergenSettingMutation = <TVariables>(action: (variables: TVariables) => Promise<GenericResult<unknown>>) => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async (variables: TVariables) => ensureOk(await action(variables)),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: settingsKeys.all });
            void queryClient.invalidateQueries({ queryKey: ['menus'] });
            void queryClient.invalidateQueries({ queryKey: ['products'] });
        },
    });
};

export const useAddAvoided = () => useAllergenSettingMutation((allergenId: string) => postAddAvoided(allergenId));
export const useAddGroup = () => useAllergenSettingMutation((groupId: string) => postAddGroup(groupId));
export const useRemoveAvoided = () => useAllergenSettingMutation((allergenId: string) => postRemoveAvoided(allergenId));
export const useAddKeyword = () =>
    useAllergenSettingMutation(({ allergenId, keyword }: { allergenId: string; keyword: string }) => postAddKeyword(allergenId, keyword));
export const useRemoveKeyword = () => useAllergenSettingMutation((keywordId: string) => postRemoveKeyword(keywordId));
export const useCreateFacilityAllergen = () =>
    useAllergenSettingMutation(({ name, keywords }: { name: string; keywords: string[] }) => postCreateFacilityAllergen(name, keywords, true));
export const useDeleteFacilityAllergen = () => useAllergenSettingMutation((allergenId: string) => postDeleteFacilityAllergen(allergenId));
