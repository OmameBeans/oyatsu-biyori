import { ENDPOINT } from '../type/endpoint';
import type { GenericResult } from '../type/genericResult';

/** API のエラー（画面に出す日本語のメッセージを持つ） */
export class ApiError extends Error {
    readonly status: number;
    readonly messages: string[];

    constructor(status: number, messages: string[]) {
        super(messages.join('\n'));
        this.name = 'ApiError';
        this.status = status;
        this.messages = messages;
    }
}

/** ログインが切れたとき・パスワード変更が必要なときに呼ぶ処理（app で登録する） */
interface AuthHandlers {
    onUnauthorized: () => void;
    onPasswordChangeRequired: () => void;
}

let authHandlers: AuthHandlers | null = null;
let antiforgeryToken: string | null = null;

/** ログインが切れたときの移動先などを登録する */
export const registerAuthHandlers = (handlers: AuthHandlers) => {
    authHandlers = handlers;
};

/** Antiforgery のトークンを捨てる（ログイン・ログアウト・パスワード変更の直後。トークンはユーザーに結びつくため） */
export const resetAntiforgeryToken = () => {
    antiforgeryToken = null;
};

const NETWORK_ERROR = '通信できませんでした。電波のよいところで、もう一度試してください。';

/** 状態コードごとの、次にすることを含むメッセージ（NFR-UX-03） */
const messageForStatus = (status: number): string => {
    switch (status) {
        case 400:
            return '入力された内容を受け付けられませんでした。内容を確かめて、もう一度試してください。';
        case 401:
            return 'ログインが必要です。ログインし直してください。';
        case 403:
            return 'パスワードの変更が必要です。設定のパスワード変更で、新しいパスワードにしてください。';
        case 404:
            return '見つかりませんでした。画面を読み込み直してください。';
        case 409:
            return '別の画面で更新されました。読み込み直してください。';
        case 413:
            return 'ファイルが大きすぎます。枚数を減らすか、小さい画像にして、もう一度試してください。';
        case 429:
            return '操作が続いたため、少し止めています。1分ほど待ってから、もう一度試してください。';
        default:
            return 'サーバーでエラーが起きました。しばらくしてから、もう一度試してください。';
    }
};

const fetchAntiforgeryToken = async (): Promise<string> => {
    const response = await fetch(ENDPOINT.AUTH_ANTIFORGERY, { credentials: 'include' });
    if (!response.ok) {
        throw new ApiError(response.status, [messageForStatus(response.status)]);
    }
    const result = (await response.json()) as GenericResult<{ token: string }>;
    antiforgeryToken = result.value?.token ?? null;
    if (!antiforgeryToken) {
        throw new ApiError(500, [messageForStatus(500)]);
    }
    return antiforgeryToken;
};

const readJson = async <T>(response: Response): Promise<GenericResult<T> | null> => {
    try {
        return (await response.json()) as GenericResult<T>;
    } catch {
        return null;
    }
};

interface SendOptions {
    method: 'GET' | 'POST';
    body?: unknown;
    form?: FormData;
}

const send = async <T>(url: string, options: SendOptions, isRetry = false): Promise<GenericResult<T>> => {
    const headers: Record<string, string> = {};
    let body: BodyInit | undefined;
    if (options.method === 'POST') {
        headers['X-XSRF-TOKEN'] = antiforgeryToken ?? (await fetchAntiforgeryToken());
        if (options.form) {
            body = options.form;
        } else {
            headers['Content-Type'] = 'application/json';
            body = JSON.stringify(options.body ?? {});
        }
    }

    let response: Response;
    try {
        response = await fetch(url, { method: options.method, credentials: 'include', headers, body });
    } catch {
        throw new ApiError(0, [NETWORK_ERROR]);
    }

    if (response.status === 400 && response.headers.get('X-Antiforgery-Error') && !isRetry) {
        // トークンが古い（ログインし直したなど）。1回だけ取り直して送り直す
        antiforgeryToken = null;
        return send<T>(url, options, true);
    }

    if (response.status === 401) {
        authHandlers?.onUnauthorized();
    }
    if (response.status === 403) {
        authHandlers?.onPasswordChangeRequired();
    }

    const result = await readJson<T>(response);
    if (!response.ok) {
        const messages = result?.errorMessages?.length ? result.errorMessages : [messageForStatus(response.status)];
        throw new ApiError(response.status, messages);
    }
    if (!result) {
        throw new ApiError(response.status, [messageForStatus(500)]);
    }
    return result;
};

/** GET（params はクエリ文字列にする） */
export const apiGet = <T>(url: string, params?: Record<string, string | number | boolean | undefined>): Promise<GenericResult<T>> => {
    const search = new URLSearchParams();
    Object.entries(params ?? {}).forEach(([key, value]) => {
        if (value !== undefined) {
            search.set(key, String(value));
        }
    });
    const query = search.toString();
    return send<T>(query ? `${url}?${query}` : url, { method: 'GET' });
};

/** POST（JSON） */
export const apiPost = <T>(url: string, body?: unknown): Promise<GenericResult<T>> => send<T>(url, { method: 'POST', body });

/** POST（multipart。画像のアップロード） */
export const apiPostForm = <T>(url: string, form: FormData): Promise<GenericResult<T>> => send<T>(url, { method: 'POST', form });

/** 結果にエラーがあれば例外にし、値を返す（クエリ・更新のフックで使う） */
export const unwrap = <T>(result: GenericResult<T>): T => {
    if (result.hasError) {
        throw new ApiError(400, result.errorMessages);
    }
    return result.value as T;
};

/** 値のない結果のエラーを例外にする */
export const ensureOk = (result: GenericResult<unknown>): void => {
    if (result.hasError) {
        throw new ApiError(400, result.errorMessages);
    }
};

/** 例外から画面に出すメッセージを取り出す */
export const errorMessageOf = (error: unknown): string => {
    if (error instanceof ApiError) {
        return error.messages.join('\n');
    }
    if (error instanceof Error && error.message) {
        return error.message;
    }
    return 'エラーが起きました。もう一度試してください。';
};
