import { useState } from 'react';
import type { DragEvent } from 'react';

/**
 * 献立案の画面のドラッグ＆ドロップで受け渡す中身（使える商品一覧 → 日のカード、日のカード → 別の日・商品一覧）。
 * 一覧と日のカードは別のスライスにあるので、受け渡しの形と、落とす先の共通の処理をここにまとめる。
 */
export type MenuDragPayload = { kind: 'product'; productId: string } | { kind: 'item'; itemId: string; productId: string; fromDate: string };

/** dataTransfer に入れる種類（ほかのドラッグ（ファイルや文字）と区別する） */
const MENU_DRAG_TYPE = 'application/x-oyatsu-menu';

/**
 * いまドラッグしているもの。dragover の間は dataTransfer の中身を読めないため、落とせる場所かどうか
 * （同じ日・もう入っている日・一覧の中の商品は光らせない）を決めるのに使う。落としたとき・やめたときに消す
 */
let currentDrag: MenuDragPayload | null = null;

export const endMenuDrag = () => {
    currentDrag = null;
    window.removeEventListener('dragend', endMenuDrag, true);
};

export const startMenuDrag = (dataTransfer: DataTransfer, payload: MenuDragPayload) => {
    dataTransfer.setData(MENU_DRAG_TYPE, JSON.stringify(payload));
    dataTransfer.effectAllowed = 'move';
    currentDrag = payload;
    // つかんだ要素がドラッグの途中で消える（読み込み直しで作り直される）と、その要素の dragend は届かない。
    // そのため、ページ全体でドラッグの終わりを拾って消す。drop は落とす先の処理より後に消すため、次の描画まで待つ
    window.addEventListener('dragend', endMenuDrag, true);
    window.addEventListener('drop', () => window.setTimeout(endMenuDrag), { capture: true, once: true });
};

const readPayload = (dataTransfer: DataTransfer): MenuDragPayload | null => {
    const raw = dataTransfer.getData(MENU_DRAG_TYPE);
    if (!raw) {
        return null;
    }
    try {
        const parsed = JSON.parse(raw) as MenuDragPayload;
        return parsed.kind === 'product' || parsed.kind === 'item' ? parsed : null;
    } catch {
        return null;
    }
};

/**
 * 落とす先の処理（日のカードと使える商品の一覧で共通）。accepts が true のドラッグだけを受け付け、その間は isOver が true になる。
 * isDisabled のあいだ（献立案の作成中・ほかの操作の保存中）は受け付けない。
 */
export const useMenuDropTarget = (accepts: (payload: MenuDragPayload) => boolean, onDrop: (payload: MenuDragPayload) => void, isDisabled: boolean) => {
    const [isOver, setIsOver] = useState(false);
    const canAccept = (dataTransfer: DataTransfer) =>
        !isDisabled && Array.from(dataTransfer.types).includes(MENU_DRAG_TYPE) && currentDrag !== null && accepts(currentDrag);

    const handlers = {
        onDragOver: (event: DragEvent) => {
            if (!canAccept(event.dataTransfer)) {
                // 途中で受け付けなくなった（保存が始まったなど）ときは、枠を消す
                setIsOver(false);
                return;
            }
            event.preventDefault();
            // 外側の落とす先（使える商品の一覧など）へ広がらないようにする
            event.stopPropagation();
            event.dataTransfer.dropEffect = 'move';
            setIsOver(true);
        },
        onDragLeave: (event: DragEvent) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
                setIsOver(false);
            }
        },
        onDrop: (event: DragEvent) => {
            event.preventDefault();
            event.stopPropagation();
            setIsOver(false);
            const payload = readPayload(event.dataTransfer);
            endMenuDrag();
            if (payload && !isDisabled && accepts(payload)) {
                onDrop(payload);
            }
        },
    };
    return { isOver, handlers };
};

/** 落とせる場所の見た目（破線の枠と薄い背景） */
export const dropTargetSx = (isOver: boolean) => ({
    outline: isOver ? 2 : 0,
    outlineColor: 'primary.main',
    outlineStyle: 'dashed' as const,
    outlineOffset: -2,
    ...(isOver ? { bgcolor: 'action.hover' } : {}),
});
