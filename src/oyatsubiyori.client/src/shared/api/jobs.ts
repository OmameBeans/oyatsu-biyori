import { useQuery } from '@tanstack/react-query';
import { ENDPOINT } from '../type/endpoint';
import { JOB_STATE } from '../type/master';
import type { JobState } from '../type/master';
import { apiGet, unwrap } from './http';

/** バックグラウンドの処理の状態（サーバーの BackgroundJobStatus） */
export interface JobStatus<T> {
    id: string;
    kind: string;
    ownerKey: string | null;
    state: JobState;
    stage: string | null;
    result: T | null;
    errorMessages: string[];
    createdAt: string;
    finishedAt: string | null;
}

export const jobKeys = {
    all: ['jobs'] as const,
    detail: (id: string) => [...jobKeys.all, id] as const,
};

/** 終わった（成功・失敗）か。見つからない ID（再起動）はサーバーが「失敗」で返す */
export const isJobFinished = (job: JobStatus<unknown> | undefined): boolean =>
    job?.state === JOB_STATE.SUCCEEDED || job?.state === JOB_STATE.FAILED;

/**
 * 処理の状態を2秒ごとに取りに行く（T0-11）。終わったら止める。
 * 「失敗（サーバーが再起動しました）」も失敗として返る。
 */
export const useJobPolling = <T>(jobId: string | null | undefined) =>
    useQuery({
        queryKey: jobKeys.detail(jobId ?? ''),
        queryFn: async () => unwrap(await apiGet<JobStatus<T>>(ENDPOINT.JOBS_GET, { id: jobId ?? '' })),
        enabled: !!jobId,
        refetchInterval: (query) => (isJobFinished(query.state.data as JobStatus<unknown> | undefined) ? false : 2000),
        refetchOnWindowFocus: false,
        retry: 2,
    });
