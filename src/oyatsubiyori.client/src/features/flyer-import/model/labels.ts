/** AI の判定の表示名 */
export const verdictLabel = (verdict: string) =>
    verdict === 'Same' ? '同じ商品' : verdict === 'FlavorVariant' ? '味違い' : verdict === 'VolumeVariant' ? '内容量違い' : '別の商品';
