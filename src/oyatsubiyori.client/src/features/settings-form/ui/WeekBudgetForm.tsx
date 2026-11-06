import { useState } from 'react';
import type { FormEvent } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import InputAdornment from '@mui/material/InputAdornment';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { useUpdateBudget } from '../../../entities/menu';
import type { MenuDetail } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';

interface WeekBudgetFormProps {
    menu: MenuDetail;
}

/** この週の予算（既定の週の予算を、この献立だけ上書きする）。検証はサーバーで行う */
export const WeekBudgetForm = ({ menu }: WeekBudgetFormProps) => {
    const [budget, setBudget] = useState(String(menu.weeklyBudget));
    const updateBudget = useUpdateBudget(menu.id);
    const { enqueueSnackbar } = useSnackbar();

    const handleSubmit = (event: FormEvent) => {
        event.preventDefault();
        updateBudget.mutate(
            { budget: Number(budget), rowVersion: menu.rowVersion },
            { onSuccess: () => enqueueSnackbar('この週の予算を保存しました', { variant: 'success' }) },
        );
    };

    return (
        <Box component="form" onSubmit={handleSubmit} noValidate>
            <Stack spacing={2}>
                <TextField
                    label="この週の予算（税込）"
                    type="number"
                    value={budget}
                    onChange={(event) => setBudget(event.target.value)}
                    slotProps={{ input: { endAdornment: <InputAdornment position="end">円</InputAdornment> }, htmlInput: { min: 1 } }}
                    sx={{ maxWidth: 320 }}
                />
                <Typography variant="body2" color="text.secondary">
                    この献立だけの予算です。ほかの週の予算は「人数・予算・重複」の既定の週の予算で決まります。
                </Typography>
                {updateBudget.isError && <Alert severity="error">{errorMessageOf(updateBudget.error)}</Alert>}
                <Box>
                    <Button type="submit" variant="contained" size="large" disabled={updateBudget.isPending}>
                        保存する
                    </Button>
                </Box>
            </Stack>
        </Box>
    );
};
