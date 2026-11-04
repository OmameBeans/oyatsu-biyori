export * from '../../shared/type/allergenJudgement';
export {
    acknowledgementSentence,
    blockedInMenuNotice,
    CATEGORY_COLOR,
    CATEGORY_EMOJI,
    CATEGORY_LABEL,
    judgementText,
    keywordHitNotice,
    manufacturerCheckText,
    needsPackageCheck,
    nonPackageSourceNotice,
} from './model/text';
export type { JudgementText } from './model/text';
export { AllergenBadge, AllergenJudgementLine, NoAvoidedAllergenNotice } from './ui/AllergenJudgementView';
