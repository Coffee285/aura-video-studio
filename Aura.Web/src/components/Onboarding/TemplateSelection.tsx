import {
  makeStyles,
  tokens,
  Title2,
  Title3,
  Text,
  Card,
  Button,
  Badge,
} from '@fluentui/react-components';
import {
  Video24Regular,
  Mic24Regular,
  Share24Regular,
  ShoppingBag24Regular,
  Play24Regular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  header: {
    textAlign: 'center',
    marginBottom: tokens.spacingVerticalL,
  },
  templatesGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))',
    gap: tokens.spacingHorizontalL,
  },
  templateCard: {
    padding: tokens.spacingVerticalXL,
    cursor: 'pointer',
    transition: 'all 0.3s ease-in-out',
    position: 'relative',
    overflow: 'hidden',
    ':hover': {
      transform: 'translateY(-4px)',
      boxShadow: tokens.shadow28,
    },
  },
  selectedCard: {
    outline: `3px solid ${tokens.colorBrandBackground}`,
    outlineOffset: '-3px',
  },
  templatePreview: {
    width: '100%',
    height: '180px',
    borderRadius: tokens.borderRadiusMedium,
    marginBottom: tokens.spacingVerticalM,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontSize: '72px',
    background: `linear-gradient(135deg, ${tokens.colorNeutralBackground3} 0%, ${tokens.colorNeutralBackground2} 100%)`,
  },
  templateHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    marginBottom: tokens.spacingVerticalM,
  },
  templateInfo: {
    flex: 1,
  },
  templateTitle: {
    marginBottom: tokens.spacingVerticalXS,
  },
  templateDescription: {
    marginBottom: tokens.spacingVerticalM,
    lineHeight: '1.5',
    color: tokens.colorNeutralForeground3,
  },
  featuresList: {
    listStyleType: 'none',
    padding: 0,
    margin: `${tokens.spacingVerticalM} 0`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  featureItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  checkIcon: {
    color: tokens.colorPaletteGreenForeground1,
  },
  templateFooter: {
    marginTop: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalM,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  statsContainer: {
    display: 'flex',
    gap: tokens.spacingHorizontalL,
  },
  stat: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  skipButton: {
    marginTop: tokens.spacingVerticalL,
    alignSelf: 'center',
  },
  infoCard: {
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground3,
    marginTop: tokens.spacingVerticalL,
  },
  popularBadge: {
    position: 'absolute',
    top: tokens.spacingVerticalM,
    right: tokens.spacingVerticalM,
  },
});

export interface VideoTemplate {
  id: string;
  name: string;
  description: string;
  icon: JSX.Element;
  emoji: string;
  features: string[];
  estimatedDuration: string;
  difficulty: 'Beginner' | 'Intermediate' | 'Advanced';
  popular?: boolean;
  presets: {
    aspectRatio: string;
    resolution: string;
    fps: number;
  };
}

export interface TemplateSelectionProps {
  templates: VideoTemplate[];
  selectedTemplateId: string | null;
  onSelectTemplate: (templateId: string) => void;
  onSkip: () => void;
  onUseTemplate: (templateId: string) => void;
}

export function TemplateSelection({
  templates,
  selectedTemplateId,
  onSelectTemplate,
  onSkip,
  onUseTemplate,
}: TemplateSelectionProps) {
  const styles = useStyles();

  const selectedTemplate = templates.find((t) => t.id === selectedTemplateId);

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Title2>Choose a Project Template</Title2>
        <Text>
          Start with a pre-configured template optimized for your content type, or skip to start
          from scratch.
        </Text>
      </div>

      <div className={styles.templatesGrid}>
        {templates.map((template) => (
          <Card
            key={template.id}
            className={`${styles.templateCard} ${selectedTemplateId === template.id ? styles.selectedCard : ''}`}
            onClick={() => onSelectTemplate(template.id)}
          >
            {template.popular && (
              <Badge className={styles.popularBadge} appearance="filled" color="brand">
                Popular
              </Badge>
            )}

            <div className={styles.templatePreview}>
              <span>{template.emoji}</span>
            </div>

            <div className={styles.templateHeader}>
              <div className={styles.templateInfo}>
                <Title3 className={styles.templateTitle}>{template.name}</Title3>
                <Badge appearance="outline" size="small">
                  {template.difficulty}
                </Badge>
              </div>
            </div>

            <Text className={styles.templateDescription} size={300}>
              {template.description}
            </Text>

            <ul className={styles.featuresList}>
              {template.features.map((feature, index) => (
                <li key={index} className={styles.featureItem}>
                  <Text size={200} className={styles.checkIcon}>
                    ✓
                  </Text>
                  <Text size={200}>{feature}</Text>
                </li>
              ))}
            </ul>

            <div className={styles.templateFooter}>
              <div className={styles.statsContainer}>
                <div className={styles.stat}>
                  <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                    Duration
                  </Text>
                  <Text size={200} weight="semibold">
                    {template.estimatedDuration}
                  </Text>
                </div>
                <div className={styles.stat}>
                  <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                    Format
                  </Text>
                  <Text size={200} weight="semibold">
                    {template.presets.aspectRatio}
                  </Text>
                </div>
              </div>

              {selectedTemplateId === template.id && (
                <Button
                  appearance="primary"
                  size="small"
                  icon={<Play24Regular />}
                  onClick={(e) => {
                    e.stopPropagation();
                    onUseTemplate(template.id);
                  }}
                >
                  Use Template
                </Button>
              )}
            </div>
          </Card>
        ))}
      </div>

      {selectedTemplate && (
        <Card className={styles.infoCard}>
          <Title3 style={{ marginBottom: tokens.spacingVerticalS }}>Template Configuration</Title3>
          <div style={{ display: 'flex', gap: tokens.spacingHorizontalXL }}>
            <div>
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Resolution:
              </Text>
              <Text size={300} weight="semibold" style={{ display: 'block' }}>
                {selectedTemplate.presets.resolution}
              </Text>
            </div>
            <div>
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Frame Rate:
              </Text>
              <Text size={300} weight="semibold" style={{ display: 'block' }}>
                {selectedTemplate.presets.fps} FPS
              </Text>
            </div>
            <div>
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Aspect Ratio:
              </Text>
              <Text size={300} weight="semibold" style={{ display: 'block' }}>
                {selectedTemplate.presets.aspectRatio}
              </Text>
            </div>
          </div>
        </Card>
      )}

      <Button className={styles.skipButton} appearance="subtle" onClick={onSkip}>
        Skip - Start from Blank Project
      </Button>
    </div>
  );
}

// Default templates
export const defaultTemplates: VideoTemplate[] = [
  {
    id: 'podcast',
    name: 'Podcast Episode',
    description: 'Perfect for audio-focused content with optional visual elements',
    icon: <Mic24Regular />,
    emoji: '🎙️',
    features: [
      'Optimized for voice clarity',
      'Waveform visualization',
      'Chapter markers support',
      'Pre-configured audio levels',
    ],
    estimatedDuration: '15-60 min',
    difficulty: 'Beginner',
    popular: true,
    presets: {
      aspectRatio: '16:9',
      resolution: '1920x1080',
      fps: 30,
    },
  },
  {
    id: 'youtube',
    name: 'YouTube Video',
    description: 'Standard YouTube format with optimal settings for the platform',
    icon: <Video24Regular />,
    emoji: '📺',
    features: [
      'YouTube-optimized export',
      '16:9 aspect ratio',
      'Intro/outro templates',
      'End screen placeholders',
    ],
    estimatedDuration: '5-20 min',
    difficulty: 'Beginner',
    popular: true,
    presets: {
      aspectRatio: '16:9',
      resolution: '1920x1080',
      fps: 30,
    },
  },
  {
    id: 'social-media',
    name: 'Social Media',
    description: 'Vertical format optimized for Instagram, TikTok, and Stories',
    icon: <Share24Regular />,
    emoji: '📱',
    features: [
      '9:16 vertical format',
      'Short-form optimized',
      'Trending effects library',
      'Quick export presets',
    ],
    estimatedDuration: '0.5-3 min',
    difficulty: 'Beginner',
    popular: true,
    presets: {
      aspectRatio: '9:16',
      resolution: '1080x1920',
      fps: 30,
    },
  },
  {
    id: 'product-demo',
    name: 'Product Demo',
    description: 'Professional product showcase with annotations and callouts',
    icon: <ShoppingBag24Regular />,
    emoji: '🛍️',
    features: [
      'Screen recording ready',
      'Annotation tools',
      'Zoom and pan effects',
      'Professional transitions',
    ],
    estimatedDuration: '2-10 min',
    difficulty: 'Intermediate',
    presets: {
      aspectRatio: '16:9',
      resolution: '1920x1080',
      fps: 60,
    },
  },
];
