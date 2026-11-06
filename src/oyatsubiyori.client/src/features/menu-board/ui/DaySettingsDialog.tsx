import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useUpdateDay } from '../../../entities/menu';
import type { DayUpdate, MenuDay, MenuDetail } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel } from '../../../shared/lib';
import { DAY_TYPE, DAY_TYPE_LABEL } from '../../../shared/type';
import type { DayType } from '../../../shared/type';

interface DaySettingsDialogProps {
    menu: MenuDetail;
    day: MenuDay;
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

/**
 * 日ごとの詳細設定（種別・人数・見出し）のダイアログ。日ごとのカードの「詳細設定」から開く。
 * 直した項目だけを送る（送った項目は「職員が直した値」になり、要望で作り直しても変えない）。開くたびに今の値から始める（呼び出し側で key を替える）。
 */
export const DaySettingsDialog = ({ menu, day, isOpen, onClose, onSuccess }: DaySettingsDialogProps) => {
    const updateDay = useUpdateDay(menu.id);
    const [dayType, setDayType] = useState<DayType>(day.dayType);
    const [countDraft, setCountDraft] = useState(String(day.childrenCount));
    const [labelDraft, setLabelDraft] = useState(day.label ?? '');
    const [error, setError] = useState<string | null>(null);
    const isEdited = day.childrenCountSource === 'EditedByStaff' || day.dayTypeEdited || day.labelEdited;

    const update: DayUpdate = { date: day.date };
    if (dayType !== day.dayType) {
        update.dayType = dayType;
    }
    if (countDraft !== String(day.childrenCount)) {
        update.childrenCount = Number(countDraft);
    }
    if (labelDraft !== (day.label ?? '')) {
        update.label = labelDraft;
        update.updateLabel = true;
    }
    const hasChanges = Object.keys(update).length > 1;

    const handleSave = () => {
        setError(null);
        updateDay.mutate({ update, rowVersion: menu.rowVersion }, { onSuccess, onError: (e) => setError(errorMessageOf(e)) });
    };

    return (
        <Dialog open={isOpen} onClose={updateDay.isPending ? undefined : onClose} maxWidth="xs" fullWidth>
            <DialogTitle>{formatDateLabel(day.date)}の詳細設定</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2}>
                    {error && <Alert severity="error">{error}</Alert>}
                    <TextField select label="種別" value={dayType} disabled={updateDay.isPending} onChange={(event) => setDayType(event.target.value as DayType)}>
                        {Object.values(DAY_TYPE).map((type) => (
                            <MenuItem key={type} value={type}>
                                {DAY_TYPE_LABEL[type]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        label="人数"
                        type="number"
                        value={countDraft}
                        disabled={updateDay.isPending}
                        onChange={(event) => setCountDraft(event.target.value)}
                        slotProps={{ htmlInput: { min: 1 } }}
                    />
                    <TextField
                        label="見出し"
                        value={labelDraft}
                        placeholder="例：誕生日会"
                        disabled={updateDay.isPending}
                        onChange={(event) => setLabelDraft(event.target.value)}
                    />
                    {isEdited && (
                        <Typography variant="caption" color="text.secondary">
                            職員が直した値があります（要望で作り直しても変えません）
                        </Typography>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose} disabled={updateDay.isPending}>
                    キャンセル
                </Button>
                <Button variant="contained" onClick={handleSave} disabled={!hasChanges || updateDay.isPending}>
                    {updateDay.isPending ? '保存しています…' : '保存する'}
                </Button>
            </DialogActions>
        </Dialog>
    );
};
