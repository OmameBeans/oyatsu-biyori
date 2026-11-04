import { apiGet, apiPost } from '../../../shared/api';
import { ENDPOINT } from '../../../shared/type';
import type { GenericResult } from '../../../shared/type';
import type { AuthStatus } from '../model/types';

export const fetchAuthStatus = (): Promise<GenericResult<AuthStatus>> => apiGet<AuthStatus>(ENDPOINT.AUTH_STATUS);

export const postLogin = (userName: string, password: string): Promise<GenericResult<AuthStatus>> =>
    apiPost<AuthStatus>(ENDPOINT.AUTH_LOGIN, { userName, password });

export const postLogout = (): Promise<GenericResult<never>> => apiPost<never>(ENDPOINT.AUTH_LOGOUT);

export const postChangePassword = (currentPassword: string, newPassword: string): Promise<GenericResult<AuthStatus>> =>
    apiPost<AuthStatus>(ENDPOINT.AUTH_CHANGE_PASSWORD, { currentPassword, newPassword });
