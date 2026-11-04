import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import { useSnackbar } from 'notistack';
import { AllergenJudgementLine, acknowledgementSentence, CATEGORY_EMOJI } from '../../../entities/allergen-judgement';
import { useAcknowledge, useAddItem, useRemoveItem, useReplaceItem, useUpdateDay, useUpdateItem } from '../../../entities/menu';
import type { ListedProduct, MenuDay, MenuDetail, MenuIssue, MenuItem as MenuItemData } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatPerChild, formatYen } from '../../../shared/lib';
import { allergenCheckPath, DAY_TYPE, DAY_TYPE_LABEL, MENU_STATUS, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { DayType } from '../../../shared/type';
import { ClaudeText } from '../../../shared/ui';

interface DayCardProps {
    menu: MenuDetail;
    day: MenuDay;
    issues: MenuIssue[];
    candidates: ListedProduct[];
    isBusy: boolean;
}

/**
 * 日ごとのカード（T4-06・T4-07・T4-13）。見出し・日の種類・人数・商品（1人あたりの個数・パック数・金額）・理由・警告。
 * 金額・判定・警告はサーバーが計算したものを出す。「メーカーへの確認が必要」はカードには出さない（03 §6.3）。
 */
export const DayCard = ({ menu, day, issues, candidates, isBusy }: DayCardProps) => {
    const updateDay = useUpdateDay(menu.id);
    const addItem = useAddItem(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [adding, setAdding] = useState('');
    const [labelDraft, setLabelDraft] = useState(day.label ?? '');
    const [countDraft, setCountDraft] = useState(String(day.childrenCount));
    const notify = (error: unknown) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' });
    const dayIssues = issues.filter((issue) => issue.menuItemId === null);
    const isPending = isBusy || updateDay.isPending || addItem.isPending;

    return (
        <Card component="section" aria-label={`${formatDateLabel(day.date)}の献立`}>
            <CardContent sx={{ '&:last-child': { pb: 2 } }}>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' }, mb: 1 }}>
                    <Typography variant="h3" component="h3" sx={{ minWidth: 90 }}>
                        {formatDateLabel(day.date)}
                    </Typography>
                    <TextField
                        select
                        label="日の種類"
                        value={day.dayType}
                        disabled={isPending}
                        onChange={(event) =>
                            updateDay.mutate({ update: { date: day.date, dayType: event.target.value as DayType }, rowVersion: menu.rowVersion }, { onError: notify })
                        }
                        sx={{ width: 130 }}
                    >
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
                        disabled={isPending}
                        onChange={(event) => setCountDraft(event.target.value)}
                        onBlur={() =>
                            countDraft !== String(day.childrenCount) &&
                            updateDay.mutate({ update: { date: day.date, childrenCount: Number(countDraft) }, rowVersion: menu.rowVersion }, { onError: notify })
                        }
                        sx={{ width: 90 }}
                    />
                    <TextField
                        label="見出し"
                        value={labelDraft}
                        placeholder="例：誕生日会"
                        disabled={isPending}
                        onChange={(event) => setLabelDraft(event.target.value)}
                        onBlur={() =>
                            labelDraft !== (day.label ?? '') &&
                            updateDay.mutate({ update: { date: day.date, label: labelDraft, updateLabel: true }, rowVersion: menu.rowVersion }, { onError: notify })
                        }
                        sx={{ flex: 1, minWidth: 120 }}
                    />
                </Stack>
                {(day.childrenCountSource === 'EditedByStaff' || day.dayTypeEdited || day.labelEdited) && (
                    <Typography variant="caption" color="text.secondary">
                        職員が直した値があります（要望で作り直しても変えません）
                    </Typography>
                )}

                <Stack spacing={1} divider={<Divider flexItem />} sx={{ mt: 1 }}>
                    {day.items.map((item) => (
                        <ItemRow key={`${item.id}-${item.perChildCount}-${item.packCount}-${item.productId}`} menu={menu} day={day} item={item} issues={issues.filter((issue) => issue.menuItemId === item.id)} candidates={candidates} isBusy={isPending} />
                    ))}
                    {day.items.length === 0 && (
                        <Typography variant="body2" color="text.secondary">
                            商品はまだありません
                        </Typography>
                    )}
                </Stack>

                <Stack direction="row" spacing={1} sx={{ mt: 1.5 }}>
                    <TextField select label="商品を足す" value={adding} onChange={(event) => setAdding(event.target.value)} sx={{ flex: 1 }} disabled={isPending}>
                        {candidates.map((candidate) => (
                            <MenuItem key={candidate.productId} value={candidate.productId}>
                                {candidate.name}（{candidate.price !== null ? formatYen(candidate.price) : '価格なし'}）{candidate.judgement.category === 'Warning' ? ` ${CATEGORY_EMOJI.Warning}` : ''}
                            </MenuItem>
                        ))}
                    </TextField>
                    <Button
                        disabled={!adding || isPending}
                        onClick={() =>
                            addItem.mutate(
                                { update: { date: day.date, productId: adding }, rowVersion: menu.rowVersion },
                                { onSuccess: () => setAdding(''), onError: notify },
                            )
                        }
                    >
                        足す
                    </Button>
                </Stack>

                <Box sx={{ mt: 1.5, display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                    <Typography sx={{ fontWeight: 700 }}>この日 {formatYen(day.amount)}</Typography>
                    <Typography color="text.secondary">1人あたり {formatPerChild(day.perChildAmount)}</Typography>
                </Box>
                {day.reason && (
                    <Box sx={{ mt: 1, p: 1, bgcolor: 'action.hover', borderRadius: 2 }}>
                        <Typography variant="caption" color="text.secondary">
                            理由
                        </Typography>
                        <ClaudeText text={day.reason} />
                    </Box>
                )}
                {dayIssues.map((issue) => (
                    <Typography key={issue.message} variant="body2" sx={{ mt: 0.5, color: 'warning.dark' }}>
                        ⚠ {issue.message}
                    </Typography>
                ))}
            </CardContent>
        </Card>
    );
};

interface ItemRowProps {
    menu: MenuDetail;
    day: MenuDay;
    item: MenuItemData;
    issues: MenuIssue[];
    candidates: ListedProduct[];
    isBusy: boolean;
}

const ItemRow = ({ menu, day, item, issues, candidates, isBusy }: ItemRowProps) => {
    const updateItem = useUpdateItem(menu.id);
    const removeItem = useRemoveItem(menu.id);
    const replaceItem = useReplaceItem(menu.id);
    const acknowledge = useAcknowledge(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [perChild, setPerChild] = useState(String(item.perChildCount));
    const [packs, setPacks] = useState(String(item.packCount));
    const [isReplacing, setIsReplacing] = useState(false);
    const notify = (error: unknown) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' });
    const isPending = isBusy || updateItem.isPending || removeItem.isPending || replaceItem.isPending;
    // アレルゲンの警告は共通部品で出すので、ここでは重複・パック数などのほかの警告だけを出す
    const otherIssues = issues.filter((issue) => issue.kind !== 'AllergenWarning' && issue.kind !== 'AllergenBlocked');

    return (
        <Box>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                        <Typography sx={{ fontWeight: 600 }}>{item.productName}</Typography>
                        {item.snackType && <Chip size="small" label={SNACK_TYPE_LABEL[item.snackType]} />}
                        {item.tags.map((tag) => (
                            <Chip key={`${tag.kind}-${tag.value}`} size="small" variant="outlined" label={tag.value} sx={{ height: 20, fontSize: 11 }} />
                        ))}
                    </Stack>
                    <Typography variant="body2" color="text.secondary">
                        {formatYen(item.unitPrice)} × {item.packCount}パック = {formatYen(item.amount)}
                        {day.excludedFromAmount ? '（金額に含まれていません）' : ''}
                    </Typography>
                </Box>
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <TextField
                        label="1人"
                        type="number"
                        value={perChild}
                        disabled={isPending}
                        onChange={(event) => setPerChild(event.target.value)}
                        onBlur={() =>
                            perChild !== String(item.perChildCount) &&
                            updateItem.mutate({ update: { itemId: item.id, perChildCount: Number(perChild) }, rowVersion: menu.rowVersion }, { onError: notify })
                        }
                        sx={{ width: 64 }}
                        slotProps={{ htmlInput: { 'aria-label': `${item.productName}の1人あたりの個数`, min: 1 } }}
                    />
                    <TextField
                        label="パック"
                        type="number"
                        value={packs}
                        disabled={isPending}
                        onChange={(event) => setPacks(event.target.value)}
                        onBlur={() =>
                            packs !== String(item.packCount) &&
                            updateItem.mutate({ update: { itemId: item.id, packCount: Number(packs) }, rowVersion: menu.rowVersion }, { onError: notify })
                        }
                        sx={{ width: 72 }}
                        slotProps={{ htmlInput: { 'aria-label': `${item.productName}のパック数`, min: 1 } }}
                    />
                    <IconButton aria-label={`${item.productName}を入れ替える`} onClick={() => setIsReplacing(!isReplacing)} disabled={isPending}>
                        <SwapHorizIcon />
                    </IconButton>
                    <IconButton
                        aria-label={`${item.productName}を外す`}
                        disabled={isPending}
                        onClick={() => removeItem.mutate({ update: { itemId: item.id }, rowVersion: menu.rowVersion }, { onError: notify })}
                    >
                        <DeleteOutlinedIcon />
                    </IconButton>
                </Stack>
            </Stack>
            {isReplacing && (
                <TextField
                    select
                    label="入れ替える商品"
                    value=""
                    fullWidth
                    sx={{ mt: 1 }}
                    onChange={(event) =>
                        replaceItem.mutate(
                            { update: { itemId: item.id, productId: event.target.value }, rowVersion: menu.rowVersion },
                            { onSuccess: () => setIsReplacing(false), onError: notify },
                        )
                    }
                >
                    {candidates
                        .filter((candidate) => candidate.productId !== item.productId)
                        .map((candidate) => (
                            <MenuItem key={candidate.productId} value={candidate.productId}>
                                {candidate.name}（{candidate.price !== null ? formatYen(candidate.price) : '価格なし'}）
                            </MenuItem>
                        ))}
                </TextField>
            )}
            {item.judgement && item.judgement.category !== 'Safe' && (
                <Box sx={{ mt: 0.5 }}>
                    <AllergenJudgementLine judgement={item.judgement} isInMenu />
                    <Button component={RouterLink} to={allergenCheckPath(item.productId)} size="small">
                        パッケージで確認する
                    </Button>
                </Box>
            )}
            {item.needsRecheck && menu.status === MENU_STATUS.CONFIRMED && item.judgement && (
                <Stack direction="row" spacing={1} sx={{ mt: 0.5, alignItems: 'center' }}>
                    <Chip size="small" color="warning" variant="outlined" label="確認し直し" />
                    <Button
                        size="small"
                        disabled={acknowledge.isPending || isBusy}
                        onClick={() =>
                            item.judgementFingerprint &&
                            acknowledge.mutate({ menuItemId: item.id, kind: item.judgement!.kind, fingerprint: item.judgementFingerprint }, { onError: notify })
                        }
                    >
                        ☐ {acknowledgementSentence(item.judgement)}
                    </Button>
                </Stack>
            )}
            {otherIssues.map((issue) => (
                <Typography key={issue.message} variant="body2" sx={{ mt: 0.5, color: issue.severity === 'Error' ? 'error.main' : 'warning.dark' }}>
                    ⚠ {issue.message}
                </Typography>
            ))}
        </Box>
    );
};
