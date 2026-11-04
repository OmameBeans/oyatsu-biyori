/** サーバーの Result / Result<T> に対応する型 */
export interface GenericResult<T> {
    value?: T | null;
    errorMessages: string[];
    hasError: boolean;
}

/** 値のない結果（サーバーの Result） */
export type EmptyResult = GenericResult<never>;
