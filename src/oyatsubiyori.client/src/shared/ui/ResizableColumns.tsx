import { useEffect, useRef, useState } from 'react';
import type { KeyboardEvent, PointerEvent, ReactNode } from 'react';
import Box from '@mui/material/Box';
import { readPreference, writePreference } from '../lib';
import type { PreferenceKey } from '../type';

/** 左右の列の幅（px） */
interface ColumnWidths {
    left: number;
    right: number;
}

interface ResizableColumnsProps {
    left: ReactNode;
    center: ReactNode;
    right: ReactNode;
    /** 左・右の列の既定の幅（px）。まん中の列は残りの幅 */
    defaultWidths: ColumnWidths;
    /** 左・右の列の最小の幅（px） */
    minWidths: ColumnWidths;
    /** まん中の列の最小の幅（px）。左右を広げても、これより狭くしない */
    minCenterWidth: number;
    /** 境目の名前（読み上げ用。例：['使える商品と献立の境目', '献立とチャットの境目']） */
    splitterLabels: [string, string];
    /** 変えた幅を保存するキー（次に開いたときも同じ幅にする） */
    storageKey: PreferenceKey;
    /**
     * false のあいだは境目を隠して1列に積む（狭い画面）。中の部品を作り直さないように（入力中の文字が消えないように）、
     * 切り替えても要素の並びは変えない
     */
    isEnabled?: boolean;
    /** true のあいだは左の列を閉じて細い帯（collapsedWidth）にし、左の境目を隠す。選んだ幅は変えない（開いたら元の幅に戻る） */
    isLeftCollapsed?: boolean;
    /** true のあいだは右の列を閉じて細い帯（collapsedWidth）にし、右の境目を隠す。選んだ幅は変えない（開いたら元の幅に戻る） */
    isRightCollapsed?: boolean;
    /** 列を閉じたときの幅（px） */
    collapsedWidth?: number;
}

const SPLITTER_WIDTH = 12;
const KEY_STEP = 24;

const isWidths = (value: unknown): value is string => typeof value === 'string' && /^\d+,\d+$/.test(value);

/**
 * 3列を並べ、列の境目（スプリッター）をドラッグして左右の列の幅を変えられるようにする。境目はキーボードでも動かせる
 * （Tab で選び、← → で動かす。Home で既定の幅に戻す）。PC の広い画面で使う。高さは親に合わせる。
 */
export const ResizableColumns = ({
    left,
    center,
    right,
    defaultWidths,
    minWidths,
    minCenterWidth,
    splitterLabels,
    storageKey,
    isEnabled = true,
    isLeftCollapsed = false,
    isRightCollapsed = false,
    collapsedWidth = 56,
}: ResizableColumnsProps) => {
    const containerRef = useRef<HTMLDivElement>(null);
    // 職員が選んだ幅（保存する値）。画面が狭いときは表示だけ縮め、この値は変えない（広げたら元の幅に戻るように）
    const [widths, setWidths] = useState<ColumnWidths>(() => {
        const [storedLeft, storedRight] = readPreference(storageKey, isWidths, `${defaultWidths.left},${defaultWidths.right}`).split(',').map(Number);
        return { left: storedLeft, right: storedRight };
    });
    const [containerWidth, setContainerWidth] = useState(0);
    const dragRef = useRef<{ side: 'left' | 'right'; startX: number; start: ColumnWidths; latest: ColumnWidths } | null>(null);

    useEffect(() => {
        const container = containerRef.current;
        if (!container) {
            return;
        }
        const observer = new ResizeObserver(() => setContainerWidth(container.clientWidth));
        observer.observe(container);
        return () => observer.disconnect();
    }, []);

    /** まん中の列が狭くなりすぎないように、片側の幅を収める */
    const clampSide = (next: ColumnWidths, side: 'left' | 'right'): ColumnWidths => {
        const min = side === 'left' ? minWidths.left : minWidths.right;
        const value = side === 'left' ? next.left : next.right;
        const other = side === 'left' ? next.right : next.left;
        const max = containerWidth > 0 ? Math.max(min, containerWidth - SPLITTER_WIDTH * 2 - minCenterWidth - other) : Number.POSITIVE_INFINITY;
        const clamped = Math.round(Math.min(Math.max(value, min), max));
        return side === 'left' ? { ...next, left: clamped } : { ...next, right: clamped };
    };
    // 表示する幅（選んだ幅を、今の画面の幅に収めたもの）
    const shown = isEnabled ? clampSide(clampSide(widths, 'right'), 'left') : widths;

    const save = (next: ColumnWidths) => writePreference(storageKey, `${next.left},${next.right}`);
    const apply = (next: ColumnWidths) => {
        setWidths(next);
        save(next);
    };

    const handlePointerDown = (side: 'left' | 'right') => (event: PointerEvent<HTMLDivElement>) => {
        event.preventDefault();
        event.currentTarget.setPointerCapture(event.pointerId);
        dragRef.current = { side, startX: event.clientX, start: shown, latest: shown };
    };
    const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
        const drag = dragRef.current;
        if (!drag) {
            return;
        }
        const delta = event.clientX - drag.startX;
        // 左の境目は右へ動かすと左の列が広がり、右の境目は左へ動かすと右の列が広がる
        const next = drag.side === 'left' ? { ...drag.start, left: drag.start.left + delta } : { ...drag.start, right: drag.start.right - delta };
        drag.latest = clampSide(next, drag.side);
        setWidths(drag.latest);
    };
    const handlePointerUp = () => {
        const drag = dragRef.current;
        if (drag) {
            dragRef.current = null;
            // 最後に動かした値を保存する（描画の前に手を離しても、画面と同じ値になるように）
            save(drag.latest);
        }
    };
    const resetSide = (side: 'left' | 'right') =>
        apply(clampSide(side === 'left' ? { ...shown, left: defaultWidths.left } : { ...shown, right: defaultWidths.right }, side));
    const handleKeyDown = (side: 'left' | 'right') => (event: KeyboardEvent<HTMLDivElement>) => {
        // → キーは境目を右へ動かす（左の列は広がり、右の列は狭まる）
        const step = event.key === 'ArrowRight' ? KEY_STEP : event.key === 'ArrowLeft' ? -KEY_STEP : 0;
        if (step !== 0) {
            event.preventDefault();
            apply(clampSide(side === 'left' ? { ...shown, left: shown.left + step } : { ...shown, right: shown.right - step }, side));
        } else if (event.key === 'Home') {
            event.preventDefault();
            resetSide(side);
        }
    };

    const isLeftOpen = !isLeftCollapsed;
    const isRightOpen = !isRightCollapsed;
    const splitter = (side: 'left' | 'right', label: string) => (
        <Box
            role="separator"
            aria-label={label}
            aria-orientation="vertical"
            aria-valuenow={side === 'left' ? shown.left : shown.right}
            aria-valuemin={side === 'left' ? minWidths.left : minWidths.right}
            tabIndex={0}
            title="ドラッグして幅を変える（ダブルクリックで元の幅に戻す）"
            onPointerDown={handlePointerDown(side)}
            onPointerMove={handlePointerMove}
            onPointerUp={handlePointerUp}
            onPointerCancel={handlePointerUp}
            onKeyDown={handleKeyDown(side)}
            onDoubleClick={() => resetSide(side)}
            sx={{
                display: isEnabled && (side === 'left' ? isLeftOpen : isRightOpen) ? 'flex' : 'none',
                cursor: 'col-resize',
                touchAction: 'none',
                justifyContent: 'center',
                borderRadius: 1,
                outline: 'none',
                // 境目の線（つかめることが分かるように、近づくと濃くする）
                '&::after': { content: '""', width: 4, my: 4, borderRadius: 2, bgcolor: 'divider', transition: 'background-color .15s' },
                '&:hover::after, &:focus-visible::after, &:active::after': { bgcolor: 'primary.main' },
            }}
        />
    );

    return (
        <Box
            ref={containerRef}
            sx={{
                display: 'grid',
                // 閉じた側の境目は隠す（display: none の要素は列を使わないので、境目の列も外し、その分を閉じた列の余白にする）
                gridTemplateColumns: !isEnabled
                    ? '1fr'
                    : [
                          isLeftOpen ? `${shown.left}px ${SPLITTER_WIDTH}px` : `${collapsedWidth + SPLITTER_WIDTH}px`,
                          `minmax(${minCenterWidth}px, 1fr)`,
                          isRightOpen ? `${SPLITTER_WIDTH}px ${shown.right}px` : `${SPLITTER_WIDTH + collapsedWidth}px`,
                      ].join(' '),
                height: '100%',
                minHeight: 0,
            }}
        >
            <Box sx={{ minWidth: 0, minHeight: 0, height: '100%', pr: isEnabled && !isLeftOpen ? `${SPLITTER_WIDTH}px` : undefined }}>{left}</Box>
            {splitter('left', splitterLabels[0])}
            <Box sx={{ minWidth: 0, minHeight: 0, height: '100%', overflow: isEnabled ? 'auto' : undefined }}>{center}</Box>
            {splitter('right', splitterLabels[1])}
            <Box sx={{ minWidth: 0, minHeight: 0, height: '100%', pl: isEnabled && !isRightOpen ? `${SPLITTER_WIDTH}px` : undefined }}>{right}</Box>
        </Box>
    );
};
