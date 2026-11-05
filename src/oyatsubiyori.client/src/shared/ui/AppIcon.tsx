import Box from '@mui/material/Box';

/** アプリのアイコン（public/ に置いたもの。ブラウザのタブのアイコンと同じ） */
const APP_ICON_SRC = '/oyatsu-menu-icon.svg';

interface AppIconProps {
    /** 幅と高さ。数値は px。見出しの中では '1.2em' のように文字の大きさに合わせて指定する */
    size: number | string;
}

/**
 * アプリのアイコン。アプリ名の横に飾りとして置くので、読み上げはしない（alt は空）。
 * 文字の行の中に置いても、文字の中央にそろう
 */
export const AppIcon = ({ size }: AppIconProps) => (
    <Box component="img" src={APP_ICON_SRC} alt="" sx={{ width: size, height: size, flexShrink: 0, verticalAlign: 'middle' }} />
);
