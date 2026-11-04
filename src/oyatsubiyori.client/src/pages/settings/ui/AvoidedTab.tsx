import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined';
import { useSnackbar } from 'notistack';
import {
    ALLERGEN_CATEGORY,
    useAddAvoided,
    useAddGroup,
    useAddKeyword,
    useAllergenMaster,
    useCreateFacilityAllergen,
    useDeleteFacilityAllergen,
    useRemoveAvoided,
    useRemoveKeyword,
} from '../../../entities/settings';
import type { AllergenItem, AllergenKeyword } from '../../../entities/settings';
import { errorMessageOf } from '../../../shared/api';
import { ConfirmDialog } from '../../../shared/ui';

const SECTIONS = [
    { category: ALLERGEN_CATEGORY.SPECIFIED, title: '特定原材料（表示義務）' },
    { category: ALLERGEN_CATEGORY.RECOMMENDED, title: '特定原材料に準ずるもの（表示推奨）' },
    { category: ALLERGEN_CATEGORY.FACILITY_ADDED, title: '施設で追加した品目（原材料の全文から探します）' },
] as const;

/**
 * SC-04 避ける品目（T2-07）。グループ → 特定原材料 → 準ずるもの → 施設の追加品目の順。
 * 外すとき・探す言葉を消すときは確認のダイアログを出す。規則（消せる言葉など）はサーバーで決める。
 */
export const AvoidedTab = () => {
    const { data: master } = useAllergenMaster();
    const { enqueueSnackbar } = useSnackbar();
    const addAvoided = useAddAvoided();
    const addGroup = useAddGroup();
    const removeAvoided = useRemoveAvoided();
    const createFacility = useCreateFacilityAllergen();
    const [removing, setRemoving] = useState<AllergenItem | null>(null);
    const [newName, setNewName] = useState('');

    const notifyError = (error: unknown) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' });
    const avoidedCount = master.allergens.filter((a) => a.isAvoided).length;

    const handleToggle = (allergen: AllergenItem) => {
        if (allergen.isAvoided) {
            setRemoving(allergen);
        } else {
            addAvoided.mutate(allergen.id, { onError: notifyError });
        }
    };

    const handleRemove = () => {
        if (!removing) {
            return;
        }
        removeAvoided.mutate(removing.id, {
            onSuccess: () => setRemoving(null),
            onError: notifyError,
        });
    };

    const handleCreate = () => {
        createFacility.mutate(
            { name: newName, keywords: [] },
            {
                onSuccess: () => {
                    enqueueSnackbar(`「${newName}」を登録して、避ける品目に入れました`, { variant: 'success' });
                    setNewName('');
                },
                onError: notifyError,
            },
        );
    };

    return (
        <Stack spacing={3} sx={{ maxWidth: 880 }}>
            {avoidedCount === 0 ? (
                <Alert severity="warning">避ける品目が設定されていません。施設で避ける品目を選んでください。</Alert>
            ) : (
                <Typography variant="body2">いま避けている品目：{avoidedCount}品目。全員が同じおやつを食べるため、ここで選んだ品目を含む商品は献立に入れられません。</Typography>
            )}
            <Box>
                <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                    グループでまとめて選ぶ
                </Typography>
                <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                    {master.groups.map((group) => {
                        const isAll = group.allergenIds.every((id) => master.allergens.find((a) => a.id === id)?.isAvoided);
                        return (
                            <Button
                                key={group.id}
                                variant={isAll ? 'contained' : 'outlined'}
                                disabled={isAll || addGroup.isPending}
                                onClick={() => addGroup.mutate(group.id, { onError: notifyError })}
                            >
                                {isAll ? `${group.name}：選択済み` : `${group.name}をまとめて避ける`}
                            </Button>
                        );
                    })}
                </Stack>
                <Typography variant="caption" color="text.secondary">
                    まとめて選んだあとで、品目ごとに外せます。
                </Typography>
            </Box>
            {SECTIONS.map((section) => (
                <Box key={section.category}>
                    <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                        {section.title}
                    </Typography>
                    <Stack spacing={1}>
                        {master.allergens
                            .filter((a) => a.category === section.category)
                            .map((allergen) => (
                                <AllergenRow key={allergen.id} allergen={allergen} onToggle={() => handleToggle(allergen)} isPending={addAvoided.isPending || removeAvoided.isPending} />
                            ))}
                    </Stack>
                    {section.category === ALLERGEN_CATEGORY.FACILITY_ADDED && (
                        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mt: 1.5 }}>
                            <TextField label="品目を追加（例：ヘーゼルナッツ）" value={newName} onChange={(event) => setNewName(event.target.value)} sx={{ minWidth: 260 }} />
                            <Button variant="outlined" onClick={handleCreate} disabled={!newName.trim() || createFacility.isPending}>
                                登録して避ける
                            </Button>
                        </Stack>
                    )}
                </Box>
            ))}
            <ConfirmDialog
                isOpen={removing !== null}
                title={`「${removing?.name ?? ''}」を避ける品目から外しますか？`}
                confirmLabel="外す"
                isDanger
                isPending={removeAvoided.isPending}
                onClose={() => setRemoving(null)}
                onConfirm={handleRemove}
            >
                <Typography>この品目を避ける子がもういないことを確認しましたか？外すと、この品目を含む商品も献立に入れられるようになります。</Typography>
            </ConfirmDialog>
        </Stack>
    );
};

interface AllergenRowProps {
    allergen: AllergenItem;
    isPending: boolean;
    onToggle: () => void;
}

const AllergenRow = ({ allergen, isPending, onToggle }: AllergenRowProps) => {
    const [isOpen, setIsOpen] = useState(false);
    const [keyword, setKeyword] = useState('');
    const [deleting, setDeleting] = useState<AllergenKeyword | null>(null);
    const [isDeletingAllergen, setIsDeletingAllergen] = useState(false);
    const addKeyword = useAddKeyword();
    const removeKeyword = useRemoveKeyword();
    const deleteAllergen = useDeleteFacilityAllergen();
    const { enqueueSnackbar } = useSnackbar();
    const notifyError = (error: unknown) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' });

    return (
        <Card sx={{ borderColor: allergen.isAvoided ? 'primary.main' : undefined }}>
            <CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}>
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <Checkbox checked={allergen.isAvoided} onChange={onToggle} disabled={isPending} slotProps={{ input: { 'aria-label': `${allergen.name}を避ける` } }} />
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography sx={{ fontWeight: allergen.isAvoided ? 700 : 400 }}>{allergen.name}</Typography>
                        {allergen.labelNotGuaranteed && (
                            <Typography variant="caption" color="text.secondary">
                                表示されないことがあります（メーカーへの確認が必要）
                            </Typography>
                        )}
                    </Box>
                    <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', sm: 'block' } }}>
                        探す言葉 {allergen.keywords.length}件
                    </Typography>
                    <IconButton aria-label={`${allergen.name}の探す言葉`} onClick={() => setIsOpen(!isOpen)} sx={{ transform: isOpen ? 'rotate(180deg)' : 'none' }}>
                        <ExpandMoreIcon />
                    </IconButton>
                </Stack>
                <Collapse in={isOpen} unmountOnExit>
                    <Box sx={{ pl: { sm: 6 }, pb: 1 }}>
                        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                            商品名と原材料の全文で、この言葉を探します（当たったら「使えません」になります）。
                        </Typography>
                        <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', mb: 1 }}>
                            {allergen.keywords.length === 0 && (
                                <Typography variant="body2" color="text.secondary">
                                    （アレルゲン表示で確かめます）
                                </Typography>
                            )}
                            {allergen.keywords.map((k) => (
                                <Chip key={k.id} label={k.keyword} size="small" onDelete={k.isSeed ? undefined : () => setDeleting(k)} variant={k.isSeed ? 'outlined' : 'filled'} />
                            ))}
                        </Stack>
                        <Stack direction="row" spacing={1}>
                            <TextField label="探す言葉を足す" value={keyword} onChange={(event) => setKeyword(event.target.value)} />
                            <Button
                                variant="outlined"
                                disabled={!keyword.trim() || addKeyword.isPending}
                                onClick={() => addKeyword.mutate({ allergenId: allergen.id, keyword }, { onSuccess: () => setKeyword(''), onError: notifyError })}
                            >
                                足す
                            </Button>
                        </Stack>
                        {allergen.category === ALLERGEN_CATEGORY.FACILITY_ADDED && allergen.canDelete && (
                            <Button color="error" startIcon={<DeleteOutlinedIcon />} sx={{ mt: 1 }} onClick={() => setIsDeletingAllergen(true)}>
                                この品目を削除する
                            </Button>
                        )}
                    </Box>
                </Collapse>
            </CardContent>
            <ConfirmDialog
                isOpen={deleting !== null}
                title={`探す言葉「${deleting?.keyword ?? ''}」を消しますか？`}
                confirmLabel="消す"
                isDanger
                isPending={removeKeyword.isPending}
                onClose={() => setDeleting(null)}
                onConfirm={() => deleting && removeKeyword.mutate(deleting.id, { onSuccess: () => setDeleting(null), onError: notifyError })}
            >
                <Typography>消すと、この言葉では原材料を探さなくなります。見落としにつながるため、本当に要らないときだけ消してください。</Typography>
            </ConfirmDialog>
            <ConfirmDialog
                isOpen={isDeletingAllergen}
                title={`「${allergen.name}」を削除しますか？`}
                confirmLabel="削除する"
                isDanger
                isPending={deleteAllergen.isPending}
                onClose={() => setIsDeletingAllergen(false)}
                onConfirm={() => deleteAllergen.mutate(allergen.id, { onSuccess: () => setIsDeletingAllergen(false), onError: notifyError })}
            >
                <Typography>施設で追加した品目を削除します。</Typography>
            </ConfirmDialog>
        </Card>
    );
};
