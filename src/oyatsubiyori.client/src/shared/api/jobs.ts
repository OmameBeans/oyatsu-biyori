import { useEffect, useState } from 'react';
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

/** 処理の流れの1件（サーバーの BackgroundJobStreamEvent）。文章は offset の位置から後ろを delta で置き換える */
interface JobStreamEvent {
    state: JobState;
    stage: string | null;
    offset: number;
    delta: string;
    errorMessages: string[];
}

/** 流れてきた処理の段階名と返答の文章 */
export interface JobStream {
    stage: string | null;
    text: string;
    state: JobState | null;
}

const EMPTY_STREAM: JobStream = { stage: null, text: '', state: null };

/**
 * 処理の段階名と返答の文章を、届いた順に受け取る（チャットの返答をパラパラ出す。D-91）。
 * 画面を離れても処理は止まらない（受け取るのをやめるだけ）。読み込み直したら、そこまでの全文から受け取り直す。
 * 終わったかどうか・結果は useJobPolling で確かめる（つながらないときも、そちらで分かる）。
 */
export const useJobStream = (jobId: string | null | undefined): JobStream => {
    const [stream, setStream] = useState<JobStream & { jobId: string | null }>({ ...EMPTY_STREAM, jobId: null });

    useEffect(() => {
        if (!jobId) {
            return;
        }
        const source = new EventSource(`${ENDPOINT.JOBS_STREAM}?id=${encodeURIComponent(jobId)}`, { withCredentials: true });
        source.addEventListener('progress', (event) => {
            const data = JSON.parse((event as MessageEvent<string>).data) as JobStreamEvent;
            setStream((previous) => {
                const base = previous.jobId === jobId ? previous.text : '';
                return { jobId, stage: data.stage, text: base.slice(0, data.offset) + data.delta, state: data.state };
            });
            if (data.state === JOB_STATE.SUCCEEDED || data.state === JOB_STATE.FAILED) {
                source.close();
            }
        });
        return () => source.close();
    }, [jobId]);

    return stream.jobId === jobId && jobId ? stream : EMPTY_STREAM;
};
