import type { ListedProduct } from '../../../entities/menu';

/** 税込価格か入数が入っていないか（入れるまで献立に入れられない。区分はサーバーが決める） */
export const isMissingValues = (item: ListedProduct) => item.exclusions.includes('MissingPrice') || item.exclusions.includes('MissingPieces');
