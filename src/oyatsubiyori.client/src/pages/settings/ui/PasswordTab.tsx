import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useSnackbar } from 'notistack';
import { useAuthStatus, useChangePassword } from '../../../entities/auth';
import { errorMessageOf } from '../../../shared/api';
import { ROUTE } from '../../../shared/type';

/**
 * SC-12 パスワード変更（T2-05）。12文字以上・文字の種類は問わない・ユーザー名と同じは不可（判定はサーバー）。
 * 初期パスワードのまま ログインした直後はこのタブを開き、変更するまでほかの画面に進めない（FR-AUTH-03b）。
 */
export const PasswordTab = () => {
    const { data: status } = useAuthStatus();
    const change = useChangePassword();
    const navigate = useNavigate();
    const { enqueueSnackbar } = useSnackbar();
    const [currentPassword, setCurrentPassword] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const isMismatch = confirmPassword !== '' && newPassword !== confirmPassword;

    const handleSubmit = (event: FormEvent) => {
        event.preventDefault();
        const wasRequired = status.mustChangePassword;
        change.mutate(
            { currentPassword, newPassword },
            {
                onSuccess: () => {
                    enqueueSnackbar('パスワードを変更しました', { variant: 'success' });
                    setCurrentPassword('');
                    setNewPassword('');
                    setConfirmPassword('');
                    if (wasRequired) {
                        navigate(ROUTE.MENUS, { replace: true });
                    }
                },
            },
        );
    };

    return (
        <Box component="form" onSubmit={handleSubmit} noValidate sx={{ maxWidth: 420 }}>
            <Stack spacing={2}>
                {status.mustChangePassword && <Alert severity="warning">初期パスワードのままです。新しいパスワードに変更してから使ってください。</Alert>}
                <Card>
                    <CardContent>
                        <Stack spacing={2}>
                            <TextField
                                label="今のパスワード"
                                type="password"
                                value={currentPassword}
                                onChange={(event) => setCurrentPassword(event.target.value)}
                                autoComplete="current-password"
                                size="medium"
                            />
                            <TextField
                                label="新しいパスワード（12文字以上）"
                                type="password"
                                value={newPassword}
                                onChange={(event) => setNewPassword(event.target.value)}
                                autoComplete="new-password"
                                helperText="文字の種類（大文字・記号など）は問いません。ユーザー名と同じものは使えません"
                                size="medium"
                            />
                            <TextField
                                label="新しいパスワード（確認）"
                                type="password"
                                value={confirmPassword}
                                onChange={(event) => setConfirmPassword(event.target.value)}
                                autoComplete="new-password"
                                error={isMismatch}
                                helperText={isMismatch ? '新しいパスワードと同じものを入れてください' : ' '}
                                size="medium"
                            />
                            {change.isError && (
                                <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>
                                    {errorMessageOf(change.error)}
                                </Alert>
                            )}
                            <Box>
                                <Button type="submit" variant="contained" size="large" disabled={change.isPending || !currentPassword || !newPassword || newPassword !== confirmPassword}>
                                    パスワードを変更する
                                </Button>
                            </Box>
                        </Stack>
                    </CardContent>
                </Card>
            </Stack>
        </Box>
    );
};
