import type { MenuListRow } from '../../../entities/menu';
import { formatYen } from '../../../shared/lib';

/** 献立一覧（SC-10）と過去の献立（SC-14）の表示で使う、部品以外のもの */

/** 四角い札の形のバッジ（sx の数値の角丸は 12px 倍になるので、px で書く） */
export const TAG_SX = { borderRadius: '4px', fontWeight: 700 } as const;

/** 画面には見せず、読み上げだけに使う */
export const VISUALLY_HIDDEN_SX = {
    // sx の 1 は 100% の意味になるので px で書く
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
    whiteSpace: 'nowrap',
} as const;

/** 差額（実績 − 予算）。超えたら＋、下回ったら−（サーバーが計算した「残り」の符号を変えて出すだけ） */
export const differenceText = (remaining: number) =>
    remaining < 0 ? `+${formatYen(-remaining)}` : remaining > 0 ? `−${formatYen(remaining)}` : `±${formatYen(0)}`;

/** 商品の問題（🔴・🟡・確認し直し）があるか */
export const hasIssues = (row: MenuListRow) => row.blockedCount > 0 || row.warningCount > 0 || row.recheckCount > 0;
