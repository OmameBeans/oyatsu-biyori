import { useState } from 'react';
import type { FormEvent } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import FormLabel from '@mui/material/FormLabel';
import InputAdornment from '@mui/material/InputAdornment';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { useSettings, useUpdateSettings } from '../../../entities/settings';
import type { DuplicateAvoidRule, Settings } from '../../../entities/settings';
import { errorMessageOf } from '../../../shared/api';
import { SNACK_TYPE, SNACK_TYPE_LABEL, WEEKDAYS, WEEKDAY_LABEL } from '../../../shared/type';
import type { SnackType, Weekday } from '../../../shared/type';

const DUPLICATE_TYPES: SnackType[] = [SNACK_TYPE.SALTY, SNACK_TYPE.SWEET, SNACK_TYPE.SMOOTH];

const toText = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));
const toNumber = (value: string): number | null => (value.trim() === '' ? null : Number(value));

interface FormState {
    childrenCount: string;
    weeklyBudget: string;
    weekdays: Weekday[];
    recheckDays: string;
    duplicate: Record<SnackType, string>;
    note: string;
}

const toForm = (settings: Settings): FormState => ({
    childrenCount: toText(settings.defaultChildrenCount),
    weeklyBudget: toText(settings.defaultWeeklyBudget),
    weekdays: settings.servingWeekdays,
    recheckDays: toText(settings.recheckDays),
    duplicate: {
        Salty: toText(settings.duplicateAvoidDays.find((r) => r.snackType === SNACK_TYPE.SALTY)?.days),
        Sweet: toText(settings.duplicateAvoidDays.find((r) => r.snackType === SNACK_TYPE.SWEET)?.days),
        Smooth: toText(settings.duplicateAvoidDays.find((r) => r.snackType === SNACK_TYPE.SMOOTH)?.days),
        NotSnack: '',
    },
    note: settings.duplicateRuleNote ?? '',
});

interface ConditionsFormProps {
    /** 保存できたとき */
    onSaved?: () => void;
}

/**
 * SC-03 条件設定（T2-06）。変更は次に作る献立から効く（作成済みの下書きは変えない。U-37）。
 * 入力の検証はサーバー（Biz の InputValidator）で行い、エラーをそのまま示す。
 * 設定画面のタブと、献立作成の手順3の「いつもの条件」のダイアログの両方で使う。
 */
export const ConditionsForm = ({ onSaved }: ConditionsFormProps) => {
    const { data: settings } = useSettings();
    const update = useUpdateSettings();
    const { enqueueSnackbar } = useSnackbar();
    const [form, setForm] = useState<FormState>(() => toForm(settings));

    const handleSubmit = (event: FormEvent) => {
        event.preventDefault();
        const rules: DuplicateAvoidRule[] = DUPLICATE_TYPES.filter((type) => form.duplicate[type].trim() !== '').map((type) => ({
            snackType: type,
            days: Number(form.duplicate[type]),
        }));
        update.mutate(
            {
                defaultChildrenCount: toNumber(form.childrenCount),
                defaultWeeklyBudget: toNumber(form.weeklyBudget),
                servingWeekdays: form.weekdays,
                recheckDays: Number(form.recheckDays),
                duplicateAvoidDays: rules,
                duplicateRuleNote: form.note,
            },
            {
                onSuccess: () => {
                    enqueueSnackbar('条件設定を保存しました', { variant: 'success' });
                    onSaved?.();
                },
            },
        );
    };

    const toggleWeekday = (weekday: Weekday) =>
        setForm((current) => ({
            ...current,
            weekdays: current.weekdays.includes(weekday) ? current.weekdays.filter((d) => d !== weekday) : [...current.weekdays, weekday],
        }));

    return (
        <Box component="form" onSubmit={handleSubmit} noValidate>
            <Stack spacing={2}>
                {!settings.isReadyForMenu && <Alert severity="warning">献立を作る前に、通常の人数と既定の週の予算を入れてください。</Alert>}
                <Stack spacing={3}>
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                        <TextField
                            label="通常の人数"
                            type="number"
                            value={form.childrenCount}
                            onChange={(event) => setForm({ ...form, childrenCount: event.target.value })}
                            slotProps={{ input: { endAdornment: <InputAdornment position="end">人</InputAdornment> }, htmlInput: { min: 1 } }}
                        />
                        <TextField
                            label="既定の週の予算（税込）"
                            type="number"
                            value={form.weeklyBudget}
                            onChange={(event) => setForm({ ...form, weeklyBudget: event.target.value })}
                            helperText="献立ごとに上書きできます"
                            slotProps={{ input: { endAdornment: <InputAdornment position="end">円</InputAdornment> }, htmlInput: { min: 1 } }}
                        />
                    </Stack>
                    <Box>
                        <FormLabel component="legend">おやつを出す曜日</FormLabel>
                        <FormGroup row>
                            {WEEKDAYS.map((weekday) => (
                                <FormControlLabel
                                    key={weekday}
                                    control={<Checkbox checked={form.weekdays.includes(weekday)} onChange={() => toggleWeekday(weekday)} />}
                                    label={WEEKDAY_LABEL[weekday]}
                                />
                            ))}
                        </FormGroup>
                    </Box>
                    <TextField
                        label="アレルゲンの再確認期間"
                        type="number"
                        value={form.recheckDays}
                        onChange={(event) => setForm({ ...form, recheckDays: event.target.value })}
                        helperText="パッケージで確認してから、この日数を過ぎた商品は「要確認」に戻ります"
                        slotProps={{ input: { endAdornment: <InputAdornment position="end">日</InputAdornment> }, htmlInput: { min: 1 } }}
                        sx={{ maxWidth: 320 }}
                    />
                    <Box>
                        <FormLabel component="legend">同じ商品を出さない期間（分類ごと。空欄は「設定なし」）</FormLabel>
                        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 1 }}>
                            {DUPLICATE_TYPES.map((type) => (
                                <TextField
                                    key={type}
                                    label={SNACK_TYPE_LABEL[type]}
                                    type="number"
                                    value={form.duplicate[type]}
                                    placeholder="設定なし"
                                    onChange={(event) => setForm({ ...form, duplicate: { ...form.duplicate, [type]: event.target.value } })}
                                    slotProps={{ input: { endAdornment: <InputAdornment position="end">日</InputAdornment> }, htmlInput: { min: 1 } }}
                                />
                            ))}
                        </Stack>
                    </Box>
                    <TextField
                        label="重複についての補足ルール（AI に伝えます）"
                        value={form.note}
                        onChange={(event) => setForm({ ...form, note: event.target.value })}
                        multiline
                        minRows={2}
                        helperText="子どもの名前は書かないでください"
                    />
                    <Typography variant="body2" color="text.secondary">
                        変更は、これから作る献立から反映されます（作成済みの下書きは変わりません）。
                    </Typography>
                    {update.isError && (
                        <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>
                            {errorMessageOf(update.error)}
                        </Alert>
                    )}
                    <Box>
                        <Button type="submit" variant="contained" size="large" disabled={update.isPending}>
                            保存する
                        </Button>
                    </Box>
                </Stack>
            </Stack>
        </Box>
    );
};
