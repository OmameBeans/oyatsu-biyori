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
    Smooth: 'デザート・飲料',
    NotSnack: '対象外',
};

/** 日の種類（画面では「種別」。サーバーの DayType） */
export const DAY_TYPE = {
    NORMAL: 'Normal',
    NO_SNACK: 'NoSnack',
} as const;
export type DayType = (typeof DAY_TYPE)[keyof typeof DAY_TYPE];

export const DAY_TYPE_LABEL: Record<DayType, string> = {
    Normal: '通常',
    NoSnack: 'おやつなし',
};

/** 献立の状態 */
export const MENU_STATUS = {
    DRAFT: 'Draft',
    CONFIRMED: 'Confirmed',
} as const;
export type MenuStatus = (typeof MENU_STATUS)[keyof typeof MENU_STATUS];

/** 献立の商品の確認の種類（サーバーの MenuItemIssueKind。D-90）。献立一覧のバッジ・商品の確認のダイアログ・日ごとのカードで同じ分け方 */
export const MENU_ITEM_ISSUE = {
    NONE: 'None',
    BLOCKED: 'Blocked',
    RECHECK: 'Recheck',
    WARNING: 'Warning',
} as const;
export type MenuItemIssue = (typeof MENU_ITEM_ISSUE)[keyof typeof MENU_ITEM_ISSUE];

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
