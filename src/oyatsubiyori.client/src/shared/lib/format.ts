import dayjs from 'dayjs';

const WEEKDAY_JA = ['日', '月', '火', '水', '木', '金', '土'];

/** 「11/12（水）」の形 */
export const formatDateLabel = (date: string): string => {
    const d = dayjs(date);
    return `${d.month() + 1}/${d.date()}（${WEEKDAY_JA[d.day()]}）`;
};

/** 「11/9（月）〜11/13（金）」の形（週の初日から） */
export const formatWeekLabel = (weekStart: string): string => {
    const start = dayjs(weekStart);
    const end = start.add(4, 'day');
    return `${start.month() + 1}/${start.date()}（月）〜${end.month() + 1}/${end.date()}（金）`;
};

/** 金額（円・3桁区切り） */
export const formatYen = (amount: number): string => `${amount.toLocaleString('ja-JP')}円`;

/** 1人あたり金額（サーバーが四捨五入した値。null は「—」） */
export const formatPerChild = (amount: number | null | undefined): string =>
    amount === null || amount === undefined ? '—' : `${amount.toLocaleString('ja-JP')}円`;

/** 日時（UTC の文字列）を日本の表示に */
export const formatDateTime = (value: string): string => dayjs(value).format('YYYY/MM/DD HH:mm');
