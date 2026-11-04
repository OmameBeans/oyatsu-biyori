import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link as RouterLink, Navigate, useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useAuthStatus, useLogin } from '../../../entities/auth';
import { errorMessageOf } from '../../../shared/api';
import { ROUTE } from '../../../shared/type';

/**
 * SC-01 ログイン（T2-05）。ログイン済みで開いたら献立一覧へ、初期パスワードのままならパスワード変更へ移す（履歴は置き換える）。
 */
export const LoginPage = () => {
    const { data: status } = useAuthStatus();
    const login = useLogin();
    const navigate = useNavigate();
    const [userName, setUserName] = useState('');
    const [password, setPassword] = useState('');

    if (status.isAuthenticated) {
        return <Navigate to={status.mustChangePassword ? ROUTE.PASSWORD_TAB : ROUTE.MENUS} replace />;
    }

    const handleSubmit = (event: FormEvent) => {
        event.preventDefault();
        login.mutate(
            { userName, password },
            {
                onSuccess: (result) => navigate(result.mustChangePassword ? ROUTE.PASSWORD_TAB : ROUTE.MENUS, { replace: true }),
            },
        );
    };

    return (
        <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', bgcolor: 'background.default', p: 2 }}>
            <Card sx={{ width: '100%', maxWidth: 400 }}>
                <CardContent sx={{ p: 3 }}>
                    <Typography variant="h1" component="h1" sx={{ mb: 0.5 }}>
                        🍪 おやつ日和
                    </Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                        ログインしてください
                    </Typography>
                    <Box component="form" onSubmit={handleSubmit} noValidate>
                        <Stack spacing={2}>
                            <TextField
                                label="ユーザー名"
                                value={userName}
                                onChange={(event) => setUserName(event.target.value)}
                                autoComplete="username"
                                autoFocus
                                required
                                size="medium"
                            />
                            <TextField
                                label="パスワード"
                                type="password"
                                value={password}
                                onChange={(event) => setPassword(event.target.value)}
                                autoComplete="current-password"
                                required
                                size="medium"
                            />
                            {login.isError && (
                                <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>
                                    {errorMessageOf(login.error)}
                                </Alert>
                            )}
                            <Button type="submit" variant="contained" size="large" disabled={login.isPending || !userName || !password}>
                                {login.isPending ? 'ログインしています…' : 'ログイン'}
                            </Button>
                        </Stack>
                    </Box>
                    <Link component={RouterLink} to={ROUTE.LANDING} variant="body2" sx={{ display: 'inline-block', mt: 2 }}>
                        おやつ日和について
                    </Link>
                </CardContent>
            </Card>
        </Box>
    );
};
