import Alert from '@mui/material/Alert';

/** ログイン後の画面に常に出す注意書き（03 §6.3、T0-14） */
export const SafetyNotice = () => (
    <Alert severity="info" icon={false} sx={{ borderRadius: 0, py: 0.25, justifyContent: 'center', fontWeight: 500 }}>
        アレルゲンの最終確認は、必ずパッケージの原材料表示で行ってください
    </Alert>
);
