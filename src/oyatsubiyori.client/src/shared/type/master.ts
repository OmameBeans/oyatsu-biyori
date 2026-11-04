/** おやつの分類（サーバーの SnackType） */
export const SNACK_TYPE = {
    SALTY: 'Salty',
    SWEET: 'Sweet',
    SMOOTH: 'Smooth',
    NOT_SNACK: 'NotSnack',
} as const;
export type SnackType = (typeof SNACK_TYPE)[keyof typeof SNACK_TYPE];

export const SNACK_TYPE_LABEL: Record<SnackType, string> = {
    Salty: 'しょっぱい',
    Sweet: 'あまい',
    Smooth: 'のどごし',
    NotSnack: '対象外',
};

/** 日の種類（サーバーの DayType） */
export const DAY_TYPE = {
    NORMAL: 'Normal',
    HANDMADE: 'Handmade',
    NO_SNACK: 'NoSnack',
} as const;
export type DayType = (typeof DAY_TYPE)[keyof typeof DAY_TYPE];

export const DAY_TYPE_LABEL: Record<DayType, string> = {
    Normal: '通常',
    Handmade: '手作り',
    NoSnack: 'おやつなし',
};

/** 献立の状態 */
export const MENU_STATUS = {
    DRAFT: 'Draft',
    CONFIRMED: 'Confirmed',
} as const;
export type MenuStatus = (typeof MENU_STATUS)[keyof typeof MENU_STATUS];

/** 曜日（サーバーの DayOfWeek） */
export const WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;
export type Weekday = (typeof WEEKDAYS)[number];
export const WEEKDAY_LABEL: Record<Weekday, string> = {
    Monday: '月',
    Tuesday: '火',
    Wednesday: '水',
    Thursday: '木',
    Friday: '金',
    Saturday: '土',
    Sunday: '日',
};

/** バックグラウンドの処理の状態 */
export const JOB_STATE = {
    QUEUED: 'Queued',
    RUNNING: 'Running',
    SUCCEEDED: 'Succeeded',
    FAILED: 'Failed',
} as const;
export type JobState = (typeof JOB_STATE)[keyof typeof JOB_STATE];
