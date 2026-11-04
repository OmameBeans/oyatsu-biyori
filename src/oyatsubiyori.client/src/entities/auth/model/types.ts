/** ログインの状態 */
export interface AuthStatus {
    isAuthenticated: boolean;
    userName: string | null;
    mustChangePassword: boolean;
}
