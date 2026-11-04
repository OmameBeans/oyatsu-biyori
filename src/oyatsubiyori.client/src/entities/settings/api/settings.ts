import { apiGet, apiPost } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { GenericResult } from '../../../shared/type';
import type { AllergenMaster, Settings, SettingsInput } from '../model/types';

export const fetchSettings = (): Promise<GenericResult<Settings>> => apiGet<Settings>(ENDPOINT.SETTINGS_GET);
export const postSettings = (input: SettingsInput): Promise<GenericResult<Settings>> => apiPost<Settings>(ENDPOINT.SETTINGS_UPDATE, input);
export const fetchAllergenMaster = (): Promise<GenericResult<AllergenMaster>> => apiGet<AllergenMaster>(ENDPOINT.SETTINGS_ALLERGENS);
export const postAddAvoided = (allergenId: string) => apiPost<never>(ENDPOINT.SETTINGS_ADD_AVOIDED, { allergenId });
export const postAddGroup = (groupId: string) => apiPost<never>(ENDPOINT.SETTINGS_ADD_GROUP, { groupId });
export const postRemoveAvoided = (allergenId: string) => apiPost<never>(ENDPOINT.SETTINGS_REMOVE_AVOIDED, { allergenId });
export const postAddKeyword = (allergenId: string, keyword: string) => apiPost<never>(ENDPOINT.SETTINGS_ADD_KEYWORD, { allergenId, keyword });
export const postRemoveKeyword = (keywordId: string) => apiPost<never>(ENDPOINT.SETTINGS_REMOVE_KEYWORD, { keywordId });
export const postCreateFacilityAllergen = (name: string, keywords: string[], avoid: boolean) =>
    apiPost<string>(ENDPOINT.SETTINGS_CREATE_FACILITY_ALLERGEN, { name, keywords, avoid });
export const postDeleteFacilityAllergen = (allergenId: string) => apiPost<never>(ENDPOINT.SETTINGS_DELETE_FACILITY_ALLERGEN, { allergenId });
