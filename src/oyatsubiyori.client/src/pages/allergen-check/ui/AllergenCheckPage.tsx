import { Link as RouterLink, useParams } from 'react-router-dom';
import Button from '@mui/material/Button';
import { AllergenJudgementLine, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { useProduct } from '../../../entities/product';
import { AllergenCheckForm } from '../../../features/allergen-check';
import { productPath } from '../../../shared/type';
import { PageLayout } from '../../../shared/ui';

/** SC-08 アレルゲン確認（T5-04）。いまの表示を上に出し、その下で確かめて記録する */
export const AllergenCheckPage = () => {
    const { productId = '' } = useParams();
    const { data: product } = useProduct(productId);
    return (
        <PageLayout
            title={`アレルゲン確認：${product.name}`}
            actions={
                <Button component={RouterLink} to={productPath(product.id)}>
                    商品の詳細へ
                </Button>
            }
            header={<AllergenJudgementLine judgement={product.judgement} isInMenu={product.usages.length > 0} showManufacturerCheck />}
        >
            {product.isAvoidedEmpty && <NoAvoidedAllergenNotice />}
            <AllergenCheckForm product={product} />
        </PageLayout>
    );
};
