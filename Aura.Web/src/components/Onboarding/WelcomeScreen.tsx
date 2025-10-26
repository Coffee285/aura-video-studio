import { makeStyles, tokens, Title1, Title3, Text, Card, Button } from '@fluentui/react-components';
import {
  VideoClip24Regular,
  Sparkle24Regular,
  Clock24Regular,
  Play24Regular,
  FolderOpen24Regular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXL,
    alignItems: 'center',
    textAlign: 'center',
    padding: tokens.spacingVerticalXXL,
  },
  heroContainer: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    marginBottom: tokens.spacingVerticalL,
    position: 'relative',
  },
  heroGraphic: {
    fontSize: '120px',
    animation: 'heroAnimation 2s ease-in-out infinite',
    textShadow: `0 0 40px ${tokens.colorBrandBackground2}`,
  },
  '@keyframes heroAnimation': {
    '0%, 100%': { transform: 'scale(1) rotate(0deg)' },
    '50%': { transform: 'scale(1.05) rotate(2deg)' },
  },
  brandContainer: {
    marginBottom: tokens.spacingVerticalXL,
  },
  title: {
    marginBottom: tokens.spacingVerticalM,
    fontSize: '42px',
    fontWeight: tokens.fontWeightBold,
  },
  subtitle: {
    maxWidth: '700px',
    marginBottom: tokens.spacingVerticalL,
    fontSize: '18px',
    lineHeight: '1.6',
    color: tokens.colorNeutralForeground2,
  },
  valuePropsContainer: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
    gap: tokens.spacingHorizontalL,
    width: '100%',
    marginTop: tokens.spacingVerticalXL,
  },
  valueCard: {
    padding: tokens.spacingVerticalXL,
    textAlign: 'center',
    transition: 'all 0.3s ease-in-out',
    background: `linear-gradient(135deg, ${tokens.colorNeutralBackground1} 0%, ${tokens.colorNeutralBackground2} 100%)`,
    ':hover': {
      transform: 'translateY(-8px)',
      boxShadow: tokens.shadow28,
    },
  },
  icon: {
    fontSize: '56px',
    marginBottom: tokens.spacingVerticalM,
    display: 'block',
    color: tokens.colorBrandBackground,
  },
  cardTitle: {
    marginBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
  },
  cardDescription: {
    color: tokens.colorNeutralForeground3,
    lineHeight: '1.5',
  },
  ctaContainer: {
    display: 'flex',
    gap: tokens.spacingHorizontalL,
    marginTop: tokens.spacingVerticalXXL,
    justifyContent: 'center',
    flexWrap: 'wrap',
  },
  primaryButton: {
    fontSize: '16px',
    padding: '12px 32px',
    height: 'auto',
  },
  secondaryButton: {
    fontSize: '16px',
    padding: '12px 32px',
    height: 'auto',
  },
  timeEstimate: {
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    marginTop: tokens.spacingVerticalXL,
    maxWidth: '500px',
  },
  estimateIcon: {
    fontSize: '24px',
    marginRight: tokens.spacingHorizontalS,
    verticalAlign: 'middle',
  },
});

export interface WelcomeScreenProps {
  onGetStarted: () => void;
  onImportProject?: () => void;
}

export function WelcomeScreen({ onGetStarted, onImportProject }: WelcomeScreenProps) {
  const styles = useStyles();

  const valuePropositions = [
    {
      icon: <VideoClip24Regular className={styles.icon} />,
      title: 'Create Professional Videos',
      description:
        'Transform your ideas into stunning, professional-quality videos in minutes with our intuitive editor',
    },
    {
      icon: <Sparkle24Regular className={styles.icon} />,
      title: 'AI-Powered Automation',
      description:
        'Leverage cutting-edge AI for script generation, voice synthesis, and custom image creation',
    },
    {
      icon: <Clock24Regular className={styles.icon} />,
      title: 'Save Valuable Time',
      description:
        'Automate repetitive tasks and focus on what matters - your creative vision and storytelling',
    },
  ];

  return (
    <div className={styles.container}>
      {/* Hero Section */}
      <div className={styles.heroContainer}>
        <div className={styles.heroGraphic}>🎬</div>
      </div>

      {/* Brand & Value Prop */}
      <div className={styles.brandContainer}>
        <Title1 className={styles.title}>Welcome to Aura Video Studio!</Title1>
        <Text className={styles.subtitle} size={500}>
          Your all-in-one platform for creating professional videos with the power of AI. Whether
          you&apos;re crafting content for YouTube, social media, or professional presentations,
          Aura Video Studio makes video creation accessible, efficient, and fun.
        </Text>
      </div>

      {/* Value Propositions Grid */}
      <div className={styles.valuePropsContainer}>
        {valuePropositions.map((prop, index) => (
          <Card key={index} className={styles.valueCard}>
            {prop.icon}
            <Title3 className={styles.cardTitle}>{prop.title}</Title3>
            <Text className={styles.cardDescription} size={300}>
              {prop.description}
            </Text>
          </Card>
        ))}
      </div>

      {/* Call to Action */}
      <div className={styles.ctaContainer}>
        <Button
          appearance="primary"
          size="large"
          className={styles.primaryButton}
          icon={<Play24Regular />}
          onClick={onGetStarted}
        >
          Get Started
        </Button>
        {onImportProject && (
          <Button
            appearance="secondary"
            size="large"
            className={styles.secondaryButton}
            icon={<FolderOpen24Regular />}
            onClick={onImportProject}
          >
            Import Existing Project
          </Button>
        )}
      </div>

      {/* Time Estimate */}
      <div className={styles.timeEstimate}>
        <Text weight="semibold" size={400}>
          <Clock24Regular className={styles.estimateIcon} />
          Quick Setup: 3-5 minutes
        </Text>
        <Text size={300} style={{ display: 'block', marginTop: tokens.spacingVerticalXS }}>
          You can pause and resume at any time. Your progress is automatically saved.
        </Text>
      </div>
    </div>
  );
}
