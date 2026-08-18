/**
 * Apple describes springs with two designer-facing numbers rather than the
 * mass/stiffness/damping triplet: a damping ratio that controls overshoot and
 * a response time. Motion's `bounce` + `duration` spring API maps onto those,
 * so these are the house values.
 *
 * The default is critically damped. Overshoot is reserved for motion that
 * follows something physical, because bounce on a panel that merely appeared
 * reads as decoration.
 */
export const springUI = { type: "spring", bounce: 0, duration: 0.4 } as const;

/** Slightly longer settle for larger surfaces arriving on screen. */
export const springEnter = { type: "spring", bounce: 0, duration: 0.55 } as const;

/** For motion that follows a gesture or a deliberate selection. */
export const springMomentum = { type: "spring", bounce: 0.2, duration: 0.4 } as const;
