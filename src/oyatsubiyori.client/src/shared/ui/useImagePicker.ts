import { useEffect, useRef, useState } from 'react';
import type { ChangeEvent, DragEvent, InputHTMLAttributes, RefObject } from 'react';
import { checkFile, isPdf, preparePdfPages, prepareImage, releaseImages, ImagePrepError } from '../lib/imagePrep';
import type { PreparedImage } from '../lib/imagePrep';

interface UseImagePickerOptions {
    images: PreparedImage[];
    onChange: (images: PreparedImage[]) => void;
    maxImages: number;
    acceptPdf: boolean;
    isDisabled?: boolean;
    /** 選んだファイルを画像にしている途中か（PDF のページ分け・変換など）が変わったとき。欄が外れたときは false を知らせる。準備中の画像はまだ images に入っていない */
    onProcessingChange?: (isProcessing: boolean) => void;
}

type HiddenInputProps = InputHTMLAttributes<HTMLInputElement> & { ref: RefObject<HTMLInputElement | null>; 'data-testid': string };

/**
 * 画像の取り込みの処理（T0-13）。見た目の違う取り込み欄（ImagePicker・チラシの取り込み欄）で同じ処理を使うためのもの。
 * 「カメラで撮る」「ファイルを選ぶ」の隠した input、ドラッグ＆ドロップ、形式・大きさ・枚数の確かめ、外す、を受け持つ。
 * 画像はこの画面のメモリにだけ持つ（D-41）。
 */
export const useImagePicker = ({ images, onChange, maxImages, acceptPdf, isDisabled, onProcessingChange }: UseImagePickerOptions) => {
    const cameraRef = useRef<HTMLInputElement>(null);
    const fileRef = useRef<HTMLInputElement>(null);
    const [errors, setErrors] = useState<string[]>([]);
    const [isProcessing, setIsProcessing] = useState(false);
    const [isDragOver, setIsDragOver] = useState(false);
    // 欄が外れたあとに準備が終わった画像は、状態に足さずに解放する（#9）
    const isMountedRef = useRef(true);
    useEffect(() => {
        isMountedRef.current = true;
        return () => {
            isMountedRef.current = false;
        };
    }, []);

    // 準備中を親に知らせる（準備中に送ると、準備できた分だけが送られるため。#9・#15）。
    // コールバックが替わったときも今の状態を知らせる。欄が外れたら準備中の印を戻す（外れるときだけ知らせるよう、最新のコールバックは ref で持つ）
    const onProcessingChangeRef = useRef(onProcessingChange);
    useEffect(() => {
        onProcessingChangeRef.current = onProcessingChange;
        onProcessingChange?.(isProcessing);
    }, [isProcessing, onProcessingChange]);
    useEffect(() => () => onProcessingChangeRef.current?.(false), []);

    const addFiles = async (files: File[]) => {
        if (files.length === 0) {
            return;
        }
        setIsProcessing(true);
        try {
            await prepareAndAdd(files);
        } finally {
            // 途中で思わぬエラーが起きても、準備中のままにしない（親の「読み取る」が押せなくなるため）
            if (isMountedRef.current) {
                setIsProcessing(false);
            }
        }
    };

    const prepareAndAdd = async (files: File[]) => {
        const messages: string[] = [];
        const added: PreparedImage[] = [];
        for (const file of files) {
            const problem = checkFile(file, acceptPdf);
            if (problem) {
                messages.push(problem);
                continue;
            }
            try {
                if (isPdf(file)) {
                    added.push(...(await preparePdfPages(file)));
                } else {
                    added.push(await prepareImage(file));
                }
            } catch (error) {
                messages.push(error instanceof ImagePrepError ? error.message : `「${file.name}」を読めませんでした。別のファイルで試してください。`);
            }
        }
        if (!isMountedRef.current) {
            releaseImages(added);
            return;
        }
        const room = Math.max(maxImages - images.length, 0);
        if (added.length > room) {
            messages.push(`1回に取り込めるのは${maxImages}枚までです。いらない画像を外してから、足してください。`);
            releaseImages(added.slice(room));
        }
        onChange([...images, ...added.slice(0, room)]);
        setErrors(messages);
    };

    const handleRemove = (id: string) => {
        releaseImages(images.filter((image) => image.id === id));
        onChange(images.filter((image) => image.id !== id));
    };

    const handleInputChange = (event: ChangeEvent<HTMLInputElement>) => {
        void addFiles(Array.from(event.target.files ?? []));
        event.target.value = '';
    };

    /** ドラッグ＆ドロップを受け付ける要素に付ける */
    const dropZoneProps = {
        onDragOver: (event: DragEvent<HTMLElement>) => {
            event.preventDefault();
            setIsDragOver(true);
        },
        onDragLeave: () => setIsDragOver(false),
        onDrop: (event: DragEvent<HTMLElement>) => {
            event.preventDefault();
            setIsDragOver(false);
            if (!isDisabled && !isProcessing) {
                void addFiles(Array.from(event.dataTransfer.files));
            }
        },
    };

    /** 「カメラで撮る」の隠した input（スマホで背面カメラを直接起動する） */
    const cameraInputProps: HiddenInputProps = {
        ref: cameraRef,
        type: 'file',
        accept: 'image/*',
        capture: 'environment',
        hidden: true,
        'data-testid': 'camera-input',
        onChange: handleInputChange,
    };

    /** 「ファイルを選ぶ」の隠した input（アルバムや PC のファイルから選ぶ） */
    const fileInputProps: HiddenInputProps = {
        ref: fileRef,
        type: 'file',
        accept: acceptPdf ? 'image/*,application/pdf,.heic,.heif' : 'image/*,.heic,.heif',
        multiple: true,
        hidden: true,
        'data-testid': 'file-input',
        onChange: handleInputChange,
    };

    return {
        errors,
        isProcessing,
        isDragOver,
        /** ボタン・外すを押せないとき（親が止めている、または画像を準備している） */
        isBusy: !!isDisabled || isProcessing,
        openCamera: () => cameraRef.current?.click(),
        openFile: () => fileRef.current?.click(),
        handleRemove,
        dropZoneProps,
        cameraInputProps,
        fileInputProps,
    };
};
