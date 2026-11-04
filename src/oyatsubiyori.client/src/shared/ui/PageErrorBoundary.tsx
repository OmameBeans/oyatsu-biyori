import { Component, Suspense } from 'react';
import type { ErrorInfo, PropsWithChildren, ReactNode } from 'react';
import { QueryErrorResetBoundary } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import { errorMessageOf } from '../api/http';
import { LoadingFallback } from './LoadingFallback';

interface BoundaryProps {
    onReset: () => void;
    fallback: (error: unknown, reset: () => void) => ReactNode;
}

interface BoundaryState {
    error: unknown;
}

class ErrorBoundary extends Component<PropsWithChildren<BoundaryProps>, BoundaryState> {
    state: BoundaryState = { error: null };

    static getDerivedStateFromError(error: unknown): BoundaryState {
        return { error };
    }

    componentDidCatch(error: unknown, info: ErrorInfo) {
        console.error('画面の表示でエラーが起きました', error, info.componentStack);
    }

    reset = () => {
        this.props.onReset();
        this.setState({ error: null });
    };

    render() {
        if (this.state.error) {
            return this.props.fallback(this.state.error, this.reset);
        }
        return this.props.children;
    }
}

/**
 * ページごとのエラーの枠（T0-10）。読み込みに失敗しても、サイドバーと注意書きは残す。
 * 中身は Suspense で囲む（useSuspenseQuery を使うため）。
 */
export const PageErrorBoundary = ({ children }: PropsWithChildren) => (
    <QueryErrorResetBoundary>
        {({ reset }) => (
            <ErrorBoundary
                onReset={reset}
                fallback={(error, retry) => (
                    <Box sx={{ p: 2 }}>
                        <Alert
                            severity="error"
                            action={
                                <Button color="inherit" size="small" onClick={retry}>
                                    もう一度読み込む
                                </Button>
                            }
                            sx={{ whiteSpace: 'pre-wrap' }}
                        >
                            {errorMessageOf(error)}
                        </Alert>
                    </Box>
                )}
            >
                <Suspense fallback={<LoadingFallback />}>{children}</Suspense>
            </ErrorBoundary>
        )}
    </QueryErrorResetBoundary>
);
