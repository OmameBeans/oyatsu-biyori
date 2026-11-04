import type { ChangeEvent } from 'react';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useAppearance } from '../../../shared/lib';
import { isColorMode, isColorTheme } from '../../../shared/type';

/**
 * 見た目：色のテーマ（#2）と明るさ（#4）を選ぶ。選ぶとすぐ画面が変わり、この端末のブラウザに保存する。
 * 色のテーマで変わるのはボタン・タブ・サイドバーの選ばれた項目などの色だけ。
 * 明るさを変えても、アレルゲンの 🔴🟡🟢 の区分と色の対応は変わらない。
 */
export const AppearanceTab = () => {
    const { colorTheme, colorThemeOptions, setColorTheme, colorMode, colorModeOptions, setColorMode } = useAppearance();

    const handleColorThemeChange = (event: ChangeEvent<HTMLInputElement>) => {
        if (isColorTheme(event.target.value)) {
            setColorTheme(event.target.value);
        }
    };

    const handleColorModeChange = (event: ChangeEvent<HTMLInputElement>) => {
        if (isColorMode(event.target.value)) {
            setColorMode(event.target.value);
        }
    };

    return (
        <Stack spacing={2} sx={{ maxWidth: 420 }}>
            <Card>
                <CardContent>
                    <FormControl>
                        <FormLabel id="color-theme-label" sx={{ fontWeight: 700, color: 'text.primary' }}>
                            色のテーマ
                        </FormLabel>
                        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 1 }}>
                            ボタンやメニューの色が変わります。この端末だけに保存されます。
                        </Typography>
                        <RadioGroup aria-labelledby="color-theme-label" value={colorTheme} onChange={handleColorThemeChange}>
                            {colorThemeOptions.map((option) => (
                                <FormControlLabel
                                    key={option.id}
                                    value={option.id}
                                    control={<Radio />}
                                    label={
                                        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
                                            <Box aria-hidden sx={{ width: 24, height: 24, borderRadius: '50%', bgcolor: option.swatch, flexShrink: 0 }} />
                                            <span>{option.label}</span>
                                        </Stack>
                                    }
                                    sx={{ minHeight: 44 }}
                                />
                            ))}
                        </RadioGroup>
                    </FormControl>
                </CardContent>
            </Card>
            <Card>
                <CardContent>
                    <FormControl>
                        <FormLabel id="color-mode-label" sx={{ fontWeight: 700, color: 'text.primary' }}>
                            明るさ
                        </FormLabel>
                        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 1 }}>
                            ダークにすると、背景が暗くなります。この端末だけに保存されます。
                        </Typography>
                        <RadioGroup aria-labelledby="color-mode-label" value={colorMode} onChange={handleColorModeChange}>
                            {colorModeOptions.map((option) => (
                                <FormControlLabel key={option.id} value={option.id} control={<Radio />} label={option.label} sx={{ minHeight: 44 }} />
                            ))}
                        </RadioGroup>
                    </FormControl>
                </CardContent>
            </Card>
        </Stack>
    );
};
