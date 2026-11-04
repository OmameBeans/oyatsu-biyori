import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query';
import { ensureOk, resetAntiforgeryToken, unwrap } from '../../../shared/api';
import { fetchAuthStatus, postChangePassword, postLogin, postLogout } from './auth';

export const authKeys = {
    all: ['auth'] as const,
    status: () => [...authKeys.all, 'status'] as const,
};

/** ログインの状態（ログイン後の枠とログイン画面でだけ使う。ランディングでは使わない） */
export const useAuthStatus = () =>
    useSuspenseQuery({
        queryKey: authKeys.status(),
        queryFn: async () => unwrap(await fetchAuthStatus()),
        staleTime: 60_000,
    });

/** ログイン（成功したら Antiforgery のトークンを取り直す） */
export const useLogin = () => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async ({ userName, password }: { userName: string; password: string }) => unwrap(await postLogin(userName, password)),
        onSuccess: (status) => {
            resetAntiforgeryToken();
            queryClient.setQueryData(authKeys.status(), status);
        },
    });
};

/** ログアウト（ほかのデータも捨てる） */
export const useLogout = () => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async () => ensureOk(await postLogout()),
        onSettled: () => {
            resetAntiforgeryToken();
            queryClient.clear();
        },
    });
};

/** パスワード変更（成功したら Antiforgery のトークンを取り直す） */
export const useChangePassword = () => {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: async ({ currentPassword, newPassword }: { currentPassword: string; newPassword: string }) =>
            unwrap(await postChangePassword(currentPassword, newPassword)),
        onSuccess: (status) => {
            resetAntiforgeryToken();
            queryClient.setQueryData(authKeys.status(), status);
        },
    });
};
