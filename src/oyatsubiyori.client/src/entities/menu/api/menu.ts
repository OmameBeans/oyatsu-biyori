import { apiGet, apiPost } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { DayType } from '../../../shared/type';
import type { AcknowledgementInput, BudgetFitOption, LatestJob, ListedProducts, MenuConfirmation, MenuDetail, MenuList } from '../model/types';

export const fetchMenuList = () => apiGet<MenuList>(ENDPOINT.MENUS_LIST);
export const fetchMenu = (id: string) => apiGet<MenuDetail>(ENDPOINT.MENUS_GET, { id });
export const fetchListedProducts = (id: string) => apiGet<ListedProducts>(ENDPOINT.MENUS_LISTED_PRODUCTS, { id });
export const fetchConfirmation = (id: string) => apiGet<MenuConfirmation>(ENDPOINT.MENUS_CONFIRMATION, { id });
export const fetchLatestProposalJob = (id: string) => apiGet<LatestJob>(ENDPOINT.MENUS_LATEST_PROPOSAL_JOB, { id });

export const postCreateMenu = (weekStart: string) => apiPost<string>(ENDPOINT.MENUS_CREATE, { weekStart });
export const postDeleteMenu = (menuId: string) => apiPost<never>(ENDPOINT.MENUS_DELETE, { menuId });
export const postSetExcluded = (menuId: string, productId: string, excluded: boolean) =>
    apiPost<never>(ENDPOINT.MENUS_SET_EXCLUDED, { menuId, productId, excluded });
export const postSetListedValues = (menuId: string, productId: string, price: number | null, piecesPerPack: number | null) =>
    apiPost<string | null>(ENDPOINT.MENUS_SET_LISTED_VALUES, { menuId, productId, price, piecesPerPack });
export const postSaveRequest = (menuId: string, requestText: string) => apiPost<never>(ENDPOINT.MENUS_SAVE_REQUEST, { menuId, requestText });
export const postStartProposal = (menuId: string, requestText: string) => apiPost<string>(ENDPOINT.MENUS_START_PROPOSAL, { menuId, requestText });
export const postChat = (menuId: string, message: string) => apiPost<string>(ENDPOINT.MENUS_CHAT, { menuId, message });
export const postBudgetFit = (menuId: string) => apiPost<string>(ENDPOINT.MENUS_BUDGET_FIT, { menuId });
export const postApplyBudgetFit = (menuId: string, option: BudgetFitOption) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_APPLY_BUDGET_FIT, {
        menuId,
        kind: option.kind,
        date: option.date,
        menuItemId: option.menuItemId,
        newProductId: option.newProductId,
        newPackCount: option.newPackCount,
        currentTotal: option.currentTotal,
    });
export const postUpdateBudget = (menuId: string, budget: number | null, rowVersion: string) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_UPDATE_BUDGET, { menuId, budget, rowVersion });

export interface DayUpdate {
    date: string;
    childrenCount?: number;
    dayType?: DayType;
    label?: string | null;
    updateLabel?: boolean;
}
export const postUpdateDay = (menuId: string, update: DayUpdate, rowVersion: string) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_UPDATE_DAY, { menuId, ...update, rowVersion });

export interface ItemUpdate {
    date?: string;
    itemId?: string;
    productId?: string;
    perChildCount?: number;
    packCount?: number;
}
export const postAddItem = (menuId: string, update: ItemUpdate, rowVersion: string) => apiPost<MenuDetail>(ENDPOINT.MENUS_ADD_ITEM, { menuId, ...update, rowVersion });
export const postReplaceItem = (menuId: string, update: ItemUpdate, rowVersion: string) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_REPLACE_ITEM, { menuId, ...update, rowVersion });
export const postRemoveItem = (menuId: string, update: ItemUpdate, rowVersion: string) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_REMOVE_ITEM, { menuId, ...update, rowVersion });
export const postUpdateItem = (menuId: string, update: ItemUpdate, rowVersion: string) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_UPDATE_ITEM, { menuId, ...update, rowVersion });
export const postConfirm = (menuId: string, acknowledgements: AcknowledgementInput[], rowVersion: string) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_CONFIRM, { menuId, acknowledgements, rowVersion });
export const postAcknowledge = (menuId: string, input: AcknowledgementInput) =>
    apiPost<MenuDetail>(ENDPOINT.MENUS_ACKNOWLEDGE, { menuId, itemId: input.menuItemId, kind: input.kind, fingerprint: input.fingerprint });
