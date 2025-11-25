import { useEffect, useRef, useState } from 'react';
import type { FC } from 'react';
import './SplashScreen.css';

interface Particle {
  x: number;
  y: number;
  size: number;
  speedY: number;
  speedX: number;
  opacity: number;
  color: string;
}

interface SplashScreenProps {
  onComplete: () => void;
  minDisplayTime?: number;
}

export const SplashScreen: FC<SplashScreenProps> = ({
  onComplete,
  minDisplayTime: _minDisplayTime = 2500,
}) => {
  const [progress, setProgress] = useState(0);
  const [stage, setStage] = useState<'loading' | 'complete' | 'fadeout'>('loading');
  const [statusMessage, setStatusMessage] = useState('Starting backend server...');

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const particlesRef = useRef<Particle[]>([]);
  const animationFrameRef = useRef<number>();
  const prefersReducedMotion = useRef(
    window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches ?? false
  );

  // Initialize particles
  useEffect(() => {
    if (prefersReducedMotion.current) {
      return;
    }

    const particleCount = 300;
    const particles: Particle[] = [];
    // Orange/blue cosmic colors matching the app icon theme
    const colors = ['#00D4FF', '#0EA5E9', '#3B82F6', '#60A5FA', '#FF6B35', '#FF8960', '#FFA07A'];

    for (let i = 0; i < particleCount; i++) {
      particles.push({
        x: Math.random() * window.innerWidth,
        y: Math.random() * window.innerHeight,
        size: Math.random() * 2 + 0.5,
        speedY: (Math.random() - 0.5) * 0.3,
        speedX: (Math.random() - 0.5) * 0.3,
        opacity: Math.random() * 0.6 + 0.2,
        color: colors[Math.floor(Math.random() * colors.length)] || colors[0],
      });
    }

    particlesRef.current = particles;
  }, []);

  // Animate particles
  useEffect(() => {
    if (prefersReducedMotion.current || !canvasRef.current) {
      return;
    }

    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const updateCanvasSize = () => {
      canvas.width = window.innerWidth;
      canvas.height = window.innerHeight;
    };

    updateCanvasSize();
    window.addEventListener('resize', updateCanvasSize);

    const animate = () => {
      if (!ctx) return;

      ctx.clearRect(0, 0, canvas.width, canvas.height);

      particlesRef.current.forEach((particle) => {
        particle.y += particle.speedY;
        particle.x += particle.speedX;

        // Wrap around edges
        if (particle.y < -10) {
          particle.y = canvas.height + 10;
        } else if (particle.y > canvas.height + 10) {
          particle.y = -10;
        }
        if (particle.x < -10) {
          particle.x = canvas.width + 10;
        } else if (particle.x > canvas.width + 10) {
          particle.x = -10;
        }

        ctx.beginPath();
        ctx.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
        ctx.fillStyle = particle.color;
        ctx.globalAlpha = particle.opacity;
        ctx.shadowBlur = 8;
        ctx.shadowColor = particle.color;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.globalAlpha = 1;
      });

      animationFrameRef.current = requestAnimationFrame(animate);
    };

    animate();

    return () => {
      window.removeEventListener('resize', updateCanvasSize);
      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }
    };
  }, []);

  useEffect(() => {
    const stages = [
      { progress: 20, message: 'Starting backend server...', delay: 300 },
      { progress: 40, message: 'Initializing services...', delay: 400 },
      { progress: 60, message: 'Loading modules...', delay: 350 },
      { progress: 80, message: 'Preparing workspace...', delay: 300 },
      { progress: 100, message: 'Almost ready...', delay: 400 },
    ];

    let currentStage = 0;
    let timeoutId: number | null = null;

    const advanceStage = () => {
      if (currentStage < stages.length) {
        const { progress: newProgress, message, delay } = stages[currentStage];
        setProgress(newProgress);
        setStatusMessage(message);
        currentStage++;
        // Schedule next stage with its specific delay
        timeoutId = window.setTimeout(advanceStage, delay);
      } else {
        setStage('complete');
        setTimeout(() => {
          setStage('fadeout');
          setTimeout(onComplete, 600);
        }, 500);
      }
    };

    // Start with the first stage's delay
    timeoutId = window.setTimeout(advanceStage, stages[0]?.delay || 300);

    return () => {
      if (timeoutId !== null) {
        clearTimeout(timeoutId);
      }
    };
  }, [onComplete]);

  return (
    <div className={`splash-screen ${stage === 'fadeout' ? 'splash-screen--fadeout' : ''}`}>
      {/* Particle canvas */}
      {!prefersReducedMotion.current && (
        <canvas ref={canvasRef} className="splash-particles-canvas" />
      )}

      {/* Animated background gradient */}
      <div className="splash-background-gradient" />

      {/* Animated grid overlay */}
      <div className="splash-grid-overlay" />

      <div className="splash-content">
        {/* Logo with film clapperboard icon */}
        <div className="splash-logo">
          <div className="splash-logo-icon">
            <svg width="120" height="120" viewBox="0 0 120 120" fill="none">
              <defs>
                {/* Cyan/blue gradient for the clapperboard glow */}
                <linearGradient id="cyanGlowGradient" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stopColor="#00D4FF" stopOpacity="1" />
                  <stop offset="50%" stopColor="#0EA5E9" stopOpacity="0.8" />
                  <stop offset="100%" stopColor="#3B82F6" stopOpacity="0.6" />
                </linearGradient>
                {/* Orange flame gradient */}
                <linearGradient id="flameGradientOrange" x1="0%" y1="100%" x2="0%" y2="0%">
                  <stop offset="0%" stopColor="#FF6B35" stopOpacity="0.9" />
                  <stop offset="40%" stopColor="#FF8960" stopOpacity="0.7" />
                  <stop offset="100%" stopColor="#FFA500" stopOpacity="0.4" />
                </linearGradient>
                {/* Blue flame gradient */}
                <linearGradient id="flameGradientCyan" x1="0%" y1="100%" x2="0%" y2="0%">
                  <stop offset="0%" stopColor="#00D4FF" stopOpacity="0.9" />
                  <stop offset="40%" stopColor="#0EA5E9" stopOpacity="0.7" />
                  <stop offset="100%" stopColor="#60A5FA" stopOpacity="0.3" />
                </linearGradient>
                {/* Clapperboard body gradient */}
                <linearGradient id="boardGradient" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stopColor="#1E3A5F" />
                  <stop offset="100%" stopColor="#0F2744" />
                </linearGradient>
                {/* Cyan edge glow */}
                <linearGradient id="edgeGlow" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stopColor="#00D4FF" stopOpacity="0.8" />
                  <stop offset="100%" stopColor="#0EA5E9" stopOpacity="0.3" />
                </linearGradient>
                <filter id="glow">
                  <feGaussianBlur stdDeviation="3" result="coloredBlur" />
                  <feMerge>
                    <feMergeNode in="coloredBlur" />
                    <feMergeNode in="SourceGraphic" />
                  </feMerge>
                </filter>
                <filter id="flameGlow">
                  <feGaussianBlur stdDeviation="2" result="coloredBlur" />
                  <feMerge>
                    <feMergeNode in="coloredBlur" />
                    <feMergeNode in="SourceGraphic" />
                  </feMerge>
                </filter>
              </defs>

              {/* Orange flames on the right side */}
              <g className="splash-flames-orange">
                <path
                  d="M 85 35 Q 90 20, 95 28 Q 92 15, 88 22 Q 85 12, 82 25 Q 80 18, 85 35 Z"
                  fill="url(#flameGradientOrange)"
                  filter="url(#flameGlow)"
                  opacity="0.9"
                />
                <path
                  d="M 78 40 Q 82 28, 86 34 Q 84 22, 80 30 Q 78 20, 75 32 Q 73 26, 78 40 Z"
                  fill="url(#flameGradientOrange)"
                  filter="url(#flameGlow)"
                  opacity="0.8"
                />
              </g>

              {/* Cyan/blue flames on the left side */}
              <g className="splash-flames-blue">
                <path
                  d="M 25 50 Q 18 35, 22 42 Q 15 30, 20 40 Q 12 28, 18 45 Q 10 38, 25 50 Z"
                  fill="url(#flameGradientCyan)"
                  filter="url(#flameGlow)"
                  opacity="0.9"
                />
                <path
                  d="M 30 55 Q 24 42, 28 48 Q 22 35, 26 45 Q 20 32, 24 50 Q 18 42, 30 55 Z"
                  fill="url(#flameGradientCyan)"
                  filter="url(#flameGlow)"
                  opacity="0.75"
                />
              </g>

              {/* Film Clapperboard - Main board with cyan glow edge */}
              <g className="splash-clapperboard">
                {/* Outer glow/border */}
                <rect
                  x="24"
                  y="38"
                  width="72"
                  height="58"
                  rx="6"
                  fill="none"
                  stroke="url(#edgeGlow)"
                  strokeWidth="2"
                  filter="url(#glow)"
                />
                {/* Main board body */}
                <rect x="25" y="39" width="70" height="56" rx="5" fill="url(#boardGradient)" />

                {/* Clapper top part with stripes */}
                <g transform="rotate(-15, 60, 35)">
                  <rect
                    x="20"
                    y="25"
                    width="75"
                    height="16"
                    rx="3"
                    fill="#1A2F4A"
                    stroke="url(#cyanGlowGradient)"
                    strokeWidth="1"
                  />
                  {/* White/cyan stripes on clapper */}
                  <rect x="25" y="28" width="8" height="10" fill="#E8F4FC" rx="1" />
                  <rect x="38" y="28" width="8" height="10" fill="#1A2F4A" rx="1" />
                  <rect x="51" y="28" width="8" height="10" fill="#E8F4FC" rx="1" />
                  <rect x="64" y="28" width="8" height="10" fill="#1A2F4A" rx="1" />
                  <rect x="77" y="28" width="8" height="10" fill="#E8F4FC" rx="1" />
                </g>

                {/* Slate text area with lines */}
                <rect x="32" y="50" width="56" height="36" rx="3" fill="#0D1B2A" />
                {/* Slate horizontal lines */}
                <line x1="38" y1="62" x2="82" y2="62" stroke="#1E3A5F" strokeWidth="1.5" />
                <line x1="38" y1="74" x2="82" y2="74" stroke="#1E3A5F" strokeWidth="1.5" />
              </g>
            </svg>
          </div>

          <h1 className="splash-title">Aura</h1>
          <p className="splash-subtitle">AI VIDEO GENERATION SUITE</p>
        </div>

        {/* Progress bar */}
        <div className="splash-progress">
          <p className="splash-status">{statusMessage}</p>
          <div className="splash-progress-track">
            <div className="splash-progress-fill" style={{ width: `${progress}%` }} />
          </div>
        </div>
      </div>
    </div>
  );
};
